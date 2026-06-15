using System;
using System.Collections.Generic;
using System.Text;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging; 
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _tokens;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<UpdateProfileRequest> _updateProfileValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
    private readonly JwtSettings _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
      AppDbContext db,
      IJwtTokenService tokens,
      IValidator<RegisterRequest> registerValidator,
      IValidator<UpdateProfileRequest> updateProfileValidator,
      IValidator<ChangePasswordRequest> changePasswordValidator,
      IOptions<JwtSettings> jwtOptions,
      ILogger<AuthService> logger) 
    {
        _db = db;
        _tokens = tokens;
        _registerValidator = registerValidator;
        _updateProfileValidator = updateProfileValidator;
        _changePasswordValidator = changePasswordValidator;
        _jwt = jwtOptions.Value;
        _logger = logger; 
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // 1) Validate input -> 400 via middleware if it fails
        var validation = await _registerValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            // Group FluentValidation errors into the shape ValidationException expects:
            // property name -> array of messages
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // 2) Reject duplicate email -> 409
        var emailTaken = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (emailTaken)
            throw new ConflictException("An account with this email already exists.");

        // 3) Hash the password with BCrypt, work factor 12 (never store plaintext)
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);

        // 4) Create the user as a Customer and save (SaveChanges assigns user.Id)
        var user = new User
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            PasswordHash = passwordHash,
            Role = UserRole.Customer
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // 5) Issue tokens, persist the refresh token, return the response
        return await IssueTokensAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // 1) Look up the user by email
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

        // 2) Same generic error for "no such user" AND "wrong password"
        //    (prevents attackers from discovering which emails exist)
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // LOG 5 (The Final Meaningful Log): Security Audit tracking authentication failures
            _logger.LogWarning("Failed login attempt detected for Identity Email: {UserEmail}", request.Email);

            throw new UnauthorizedException("Invalid email or password.");
        }

        // 3) Issue tokens and return
        return await IssueTokensAsync(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        // 1) Look up the refresh token by its string value, and eager-load its owning User
        //    (.Include) because IssueTokensAsync needs the actual User object, not just an Id.
        var existing = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        // 2) Validate — every failure is the SAME generic 401 (don't leak which check failed).
        if (existing is null)
            throw new UnauthorizedException("Invalid refresh token.");

        if (existing.RevokedAt is not null)            // already revoked (rotated or logged out)
            throw new UnauthorizedException("Refresh token has been revoked.");

        if (existing.ExpiresAt <= DateTime.UtcNow)     // past its 7-day life
            throw new UnauthorizedException("Refresh token has expired.");

        // 3) ROTATION: kill the old token so it can never be reused.
        //    EF Core is tracking 'existing', so this change is saved by the SaveChanges
        //    that happens inside IssueTokensAsync below — both writes in one transaction.
        existing.RevokedAt = DateTime.UtcNow;

        // 4) Issue a brand-new access + refresh token pair for the same user, persist, return.
        return await IssueTokensAsync(existing.User);
    }

    public async Task LogoutAsync(LogoutRequest request)
    {
        // Find the token. Logout is IDEMPOTENT: an unknown or already-revoked token is NOT
        // an error — the desired end state ("this token is dead") is already true, so we
        // simply return and let the controller send 204.
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
    public async Task<UserProfileResponse> GetProfileAsync(int userId)
    {
        // Load the user whose id came from the validated JWT "sub" claim.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            throw new NotFoundException("User not found.");   // token valid, but account gone -> 404

        return MapToProfile(user);
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        // 1) Validate input -> 400 via middleware if it fails (same pattern as RegisterAsync).
        var validation = await _updateProfileValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // 2) Load the caller's own row.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            throw new NotFoundException("User not found.");

        // 3) Copy ONLY the editable fields. (Email and Role are intentionally not touchable here.)
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.PhoneNumber = request.PhoneNumber;

        // 4) Change tracking already marked 'user' Modified; one UPDATE is sent here.
        await _db.SaveChangesAsync();

        return MapToProfile(user);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        // 1) Validate input shape -> 400 if bad (non-empty, length, new != current).
        var validation = await _changePasswordValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // 2) Load the caller's own row.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            throw new NotFoundException("User not found.");

        // 3) Re-verify the CURRENT password against the stored hash -> 401 if wrong (3.8).
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedException("Current password is incorrect.");

        // 4) Hash the NEW password (work factor 12, like RegisterAsync).
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12);

        // 5) Revoke ALL of this user's still-active refresh tokens so every existing
        //    session is logged out. A password change often means "I may be compromised";
        //    an attacker holding an old refresh token must not be able to keep minting
        //    access tokens after the password changes. EF is tracking each loaded token,
        //    so these revocations and the password update are saved together by the single
        //    SaveChanges below (one atomic transaction).
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var token in activeTokens)
            token.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    // Map the entity to its safe public projection (no PasswordHash, no navigation graph).
    private static UserProfileResponse MapToProfile(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
        Role = user.Role.ToString(),
        CreatedAt = user.CreatedAt
    };

    // Shared helper: mint access + refresh tokens, store the refresh token, build AuthResponse.
    private async Task<AuthResponse> IssueTokensAsync(User user)
    {
        var accessToken = _tokens.GenerateAccessToken(user);
        var refreshTokenValue = _tokens.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)   // refresh token lives 7 days
        };
        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresIn = _jwt.ExpiryMinutes * 60,     // minutes -> seconds (15 -> 900)
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role.ToString()
            }
        };
    }
}
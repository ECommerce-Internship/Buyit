using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Constants;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buyit.Infrastructure.Services;

/// <summary>
/// Runs after Google has verified a user. Finds the matching account (returning
/// user) or creates a new one (first login), then issues tokens. See TB-75.
/// </summary>
public class ExternalAuthService : IExternalAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _tokens;
    private readonly JwtSettings _jwt;

    public ExternalAuthService(
        AppDbContext db,
        IJwtTokenService tokens,
        IOptions<JwtSettings> jwtOptions)
    {
        _db = db;
        _tokens = tokens;
        _jwt = jwtOptions.Value;
    }

    public async Task<AuthResponse> FindOrCreateUserAsync(GoogleClaims claims)
    {
        // 0) GUARD: Google's "sub" is the user's permanent identity. If it is missing
        //    or blank, we cannot reliably identify or create the account, so reject the
        //    request as invalid input (-> 400 Bad Request via the exception middleware).
        if (string.IsNullOrWhiteSpace(claims.Subject))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Subject"] = new[]
                {
                   "Google did not return a subject ('sub') claim, so the user cannot be identified."
               }
            });
        }

        // 1) Returning user? Match the existing Google link on Provider + sub.
        var existingLogin = await _db.UserExternalLogins
            .Include(el => el.User)
            .FirstOrDefaultAsync(el =>
                el.Provider == AuthProviders.Google &&
                el.ProviderUserId == claims.Subject);

        if (existingLogin is not null)
            return await IssueTokensAsync(existingLogin.User);


        // 2) First Google login — is the email already a password account?
        var userWithSameEmail = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == claims.Email);

        if (userWithSameEmail is not null)
        {
            // 3) Email collision -> block with a clear 409 (no silent merge).
            throw new ConflictException(
                "An account with this email already exists. " +
                "Please sign in with your email and password instead.");
        }

        // 4) Brand-new user: create User (no password) + link row.
        var (firstName, lastName) = SplitName(claims.Name);

        var newUser = new User
        {
            Email = claims.Email,
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Customer,
            PasswordHash = null
        };

        var externalLogin = new UserExternalLogin
        {
            Provider = AuthProviders.Google,
            ProviderUserId = claims.Subject,
            User = newUser
        };

        _db.Users.Add(newUser);
        _db.UserExternalLogins.Add(externalLogin);

        // Persist the new User + link FIRST so the database assigns newUser.Id.
        // Without this, the access token's "sub" claim and the refresh token's
        // UserId would both be 0, causing an FK violation on the refresh token.
        // (Mirrors AuthService.RegisterAsync, which also saves before issuing tokens.)
        await _db.SaveChangesAsync();

        // Now newUser.Id is real — mint the tokens.
        return await IssueTokensAsync(newUser);
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return ("Google", "User");

        var parts = fullName.Trim().Split(' ', 2);
        var first = parts[0];
        var last = parts.Length > 1 ? parts[1] : "";
        return (first, last);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user)
    {
        var accessToken = _tokens.GenerateAccessToken(user);
        var refreshTokenValue = _tokens.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.RefreshTokens.Add(refreshToken);

        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresIn = _jwt.ExpiryMinutes * 60,
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
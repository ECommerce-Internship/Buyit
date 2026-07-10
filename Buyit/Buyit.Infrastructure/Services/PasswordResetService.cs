using System.Security.Cryptography;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Common;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

public class PasswordResetService : IPasswordResetService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IValidator<ForgotPasswordRequest> _forgotValidator;
    private readonly IValidator<ResetPasswordRequest> _resetValidator;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        AppDbContext db,
        IEmailService email,
        IValidator<ForgotPasswordRequest> forgotValidator,
        IValidator<ResetPasswordRequest> resetValidator,
        ILogger<PasswordResetService> logger)
    {
        _db = db;
        _email = email;
        _forgotValidator = forgotValidator;
        _resetValidator = resetValidator;
        _logger = logger;
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request)
    {
        var validation = await _forgotValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        var email = EmailNormalizer.Normalize(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Account enumeration guard: ALWAYS complete successfully whether or not this email
        // has an account. Only actually generate/send a code when it does.
        if (user is null)
        {
            _logger.LogInformation("Password reset requested for an unknown email.");
            return;
        }

        // Google-only accounts have no password to reset — same silent no-op, for the same
        // reason (don't leak account existence OR sign-in method via a different response).
        if (user.PasswordHash is null)
        {
            _logger.LogInformation("Password reset requested for Google-only account {UserId} — no-op.", user.Id);
            return;
        }

        // Cryptographically secure RNG — a predictable RNG would let an attacker guess codes
        // for accounts they're targeting.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var codeHash = BCrypt.Net.BCrypt.HashPassword(code, 10);

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        await _db.SaveChangesAsync();

        // The RAW code is only ever emailed, never persisted. Email implementations already
        // fail-open internally (log + swallow), so no extra try/catch is needed here.
        await _email.SendPasswordResetCodeAsync(user.Email, code);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var validation = await _resetValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        var email = EmailNormalizer.Normalize(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Same generic error whether the email is unknown, the code is wrong, expired, or
        // already used — never reveal WHICH check failed.
        if (user is null)
            throw new UnauthorizedException("Invalid or expired reset code.");

        var candidates = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var match = candidates.FirstOrDefault(t => BCrypt.Net.BCrypt.Verify(request.Code, t.CodeHash));
        if (match is null)
            throw new UnauthorizedException("Invalid or expired reset code.");

        // Mark used FIRST — single-use, even if something below somehow fails.
        match.UsedAt = DateTime.UtcNow;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12);

        // A password reset is a strong signal the account may have been compromised — revoke
        // every active session so only whoever just reset it stays signed in. Mirrors
        // AuthService.ChangePasswordAsync's exact reasoning.
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync();
        foreach (var rt in activeTokens)
            rt.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}

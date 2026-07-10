using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Application.Validators;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Buyit.Tests;

public class PasswordResetServiceTests
{
    private static PasswordResetService BuildSut(out AppDbContext db, out Mock<IEmailService> emailMock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);
        emailMock = new Mock<IEmailService>();

        return new PasswordResetService(
            db,
            emailMock.Object,
            new ForgotPasswordRequestValidator(),
            new ResetPasswordRequestValidator(),
            Mock.Of<ILogger<PasswordResetService>>());
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, string password = "OldPassword123!")
    {
        var user = new User
        {
            FirstName = "Test",
            LastName = "User",
            Email = "reset-test@buyit.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task RequestReset_KnownEmail_SendsCodeAndPersistsHashedToken()
    {
        var sut = BuildSut(out var db, out var emailMock);
        var user = await SeedUserAsync(db);
        string? sentCode = null;
        emailMock
            .Setup(e => e.SendPasswordResetCodeAsync(user.Email, It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);

        await sut.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = user.Email });

        sentCode.Should().NotBeNullOrEmpty();
        sentCode.Should().MatchRegex("^[0-9]{6}$");

        var token = await db.PasswordResetTokens.SingleAsync();
        token.UserId.Should().Be(user.Id);
        token.CodeHash.Should().NotBe(sentCode); // never stored raw
        token.UsedAt.Should().BeNull();
        token.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        emailMock.Verify(e => e.SendPasswordResetCodeAsync(user.Email, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RequestReset_UnknownEmail_CompletesSuccessfullyAndSendsNoEmail()
    {
        var sut = BuildSut(out var db, out var emailMock);

        Func<Task> act = async () =>
            await sut.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "nobody@buyit.com" });

        await act.Should().NotThrowAsync(); // must complete successfully — no account enumeration

        emailMock.Verify(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        (await db.PasswordResetTokens.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ResetPassword_HappyPath_AllowsNewPasswordAndRejectsOld()
    {
        var sut = BuildSut(out var db, out var emailMock);
        var user = await SeedUserAsync(db, password: "OldPassword123!");
        string? sentCode = null;
        emailMock
            .Setup(e => e.SendPasswordResetCodeAsync(user.Email, It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);

        await sut.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = user.Email });
        sentCode.Should().NotBeNullOrEmpty();

        await sut.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = user.Email,
            Code = sentCode!,
            NewPassword = "NewPassword456!"
        });

        var updated = await db.Users.SingleAsync(u => u.Id == user.Id);
        BCrypt.Net.BCrypt.Verify("NewPassword456!", updated.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("OldPassword123!", updated.PasswordHash).Should().BeFalse();
    }

    [Fact]
    public async Task ResetPassword_AfterSuccess_RevokesAllActiveRefreshTokens()
    {
        var sut = BuildSut(out var db, out var emailMock);
        var user = await SeedUserAsync(db);

        db.RefreshTokens.Add(new RefreshToken { Token = "rt-1", UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(7) });
        db.RefreshTokens.Add(new RefreshToken { Token = "rt-2", UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await db.SaveChangesAsync();

        string? sentCode = null;
        emailMock
            .Setup(e => e.SendPasswordResetCodeAsync(user.Email, It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);
        await sut.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = user.Email });

        await sut.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = user.Email,
            Code = sentCode!,
            NewPassword = "NewPassword456!"
        });

        var tokens = await db.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        tokens.Should().OnlyContain(rt => rt.RevokedAt != null);
    }

    [Fact]
    public async Task ResetPassword_WrongCode_ThrowsUnauthorized()
    {
        var sut = BuildSut(out var db, out var emailMock);
        var user = await SeedUserAsync(db);
        emailMock
            .Setup(e => e.SendPasswordResetCodeAsync(user.Email, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        await sut.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = user.Email });

        Func<Task> act = async () => await sut.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = user.Email,
            Code = "000000",
            NewPassword = "NewPassword456!"
        });

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ResetPassword_ExpiredCode_ThrowsUnauthorized()
    {
        var sut = BuildSut(out var db, out var emailMock);
        var user = await SeedUserAsync(db);

        var code = "123456";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code, 10),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // already expired
        });
        await db.SaveChangesAsync();

        Func<Task> act = async () => await sut.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = user.Email,
            Code = code,
            NewPassword = "NewPassword456!"
        });

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ResetPassword_AlreadyUsedCode_ThrowsUnauthorized()
    {
        var sut = BuildSut(out var db, out var emailMock);
        var user = await SeedUserAsync(db);

        var code = "654321";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code, 10),
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            UsedAt = DateTime.UtcNow.AddMinutes(-1), // already used
        });
        await db.SaveChangesAsync();

        Func<Task> act = async () => await sut.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = user.Email,
            Code = code,
            NewPassword = "NewPassword456!"
        });

        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
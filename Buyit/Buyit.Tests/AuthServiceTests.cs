using FluentAssertions;                       // .Should() readable assertions
using Microsoft.EntityFrameworkCore;          // DbContextOptionsBuilder, UseInMemoryDatabase
using Microsoft.Extensions.Options;           // Options.Create(...)
using Microsoft.Extensions.Logging;           // ILogger<T>
using Moq;                                    // Mock<T>, It.IsAny, Returns
using Xunit;                                  // [Fact]

using Buyit.Application.Common;               // JwtSettings
using Buyit.Application.DTOs;                 // RegisterRequest, LoginRequest, RefreshTokenRequest, AuthResponse
using Buyit.Application.Interfaces;           // IJwtTokenService
using Buyit.Application.Validators;           // RegisterRequestValidator, etc.
using Buyit.Domain.Entities;                  // User, RefreshToken
using Buyit.Domain.Enums;                     // UserRole
using Buyit.Domain.Exceptions;                // ConflictException, UnauthorizedException
using Buyit.Infrastructure.Data;              // AppDbContext
using Buyit.Infrastructure.Services;          // AuthService (the system under test)

namespace Buyit.Tests;

public class AuthServiceTests
{
    private static AuthService BuildSut(out AppDbContext db)
    {
        // A brand-new, uniquely-named in-memory store for THIS test only.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);

        // Fake the JWT service: any user -> these constant strings.
        var jwtMock = new Mock<IJwtTokenService>();
        jwtMock.Setup(s => s.GenerateAccessToken(It.IsAny<User>()))
               .Returns("fake-access-token");
        jwtMock.Setup(s => s.GenerateRefreshToken())
               .Returns("fake-refresh-token");

        // Real validators (they have no dependencies).
        var registerValidator = new RegisterRequestValidator();
        var updateValidator = new UpdateProfileRequestValidator();
        var changeValidator = new ChangePasswordRequestValidator();

        // Minimal settings; only ExpiryMinutes is read by AuthService.
        var jwtOptions = Options.Create(new JwtSettings { ExpiryMinutes = 15 });

        var loggerMock = new Mock<ILogger<AuthService>>();

        return new AuthService(
            db,
            jwtMock.Object,
            registerValidator,
            updateValidator,
            changeValidator,
            jwtOptions,
            loggerMock.Object);
    }

    // Convenience: build a valid registration request that PASSES the real validator.
    private static RegisterRequest ValidRegisterRequest() => new()
    {
        Email = "newuser@example.com",
        Password = "Password123",      // >= 8 chars, satisfies the validator
        FirstName = "New",
        LastName = "User",
        PhoneNumber = "+1234567890"
    };

    // -----------------------------------------------------------------
    // TEST 1: A valid registration returns an AuthResponse with the
    //         correct email and the Customer role.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterAsync_ValidRequest_ReturnsAuthResponse()
    {
        // Arrange
        var sut = BuildSut(out _);          // empty db: no existing users
        var request = ValidRegisterRequest();

        // Act
        AuthResponse result = await sut.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.User.Email.Should().Be("newuser@example.com");
        result.User.Role.Should().Be("Customer");
        result.AccessToken.Should().Be("fake-access-token");
        result.RefreshToken.Should().Be("fake-refresh-token");
    }

    // -----------------------------------------------------------------
    // TEST 2: Registering with an email that already exists throws
    //         ConflictException.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsConflictException()
    {
        // Arrange: pre-seed a user with the SAME email the request will use.
        var sut = BuildSut(out var db);
        db.Users.Add(new User
        {
            Email = "newuser@example.com",
            FirstName = "Existing",
            LastName = "Person",
            PasswordHash = "irrelevant-hash",
            Role = UserRole.Customer
        });
        await db.SaveChangesAsync();

        var request = ValidRegisterRequest();   // same email -> duplicate

        // Act
        Func<Task> act = async () => await sut.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    // -----------------------------------------------------------------
    // TEST 3: Correct email + password returns tokens.
    //         The seeded user must have a REAL BCrypt hash of the password.
    // -----------------------------------------------------------------
    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        // Arrange
        var sut = BuildSut(out var db);
        const string email = "login@example.com";
        const string password = "Password123";

        db.Users.Add(new User
        {
            Email = email,
            FirstName = "Log",
            LastName = "In",
            // Hash the known password the same way AuthService does.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            Role = UserRole.Customer
        });
        await db.SaveChangesAsync();

        var request = new LoginRequest { Email = email, Password = password };

        // Act
        AuthResponse result = await sut.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("fake-access-token");
        result.RefreshToken.Should().Be("fake-refresh-token");
        result.User.Email.Should().Be(email);
    }

    // -----------------------------------------------------------------
    // TEST 4: Wrong password throws UnauthorizedException.
    // -----------------------------------------------------------------
    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedException()
    {
        // Arrange
        var sut = BuildSut(out var db);
        db.Users.Add(new User
        {
            Email = "login@example.com",
            FirstName = "Log",
            LastName = "In",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPassword1", 12),
            Role = UserRole.Customer
        });
        await db.SaveChangesAsync();

        var request = new LoginRequest
        {
            Email = "login@example.com",
            Password = "WrongPassword1"        // does NOT match the stored hash
        };

        // Act
        Func<Task> act = async () => await sut.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    // -----------------------------------------------------------------
    // TEST 5: An expired refresh token throws UnauthorizedException.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        // Arrange
        var sut = BuildSut(out var db);
        var user = new User
        {
            Email = "refresh@example.com",
            FirstName = "Re",
            LastName = "Fresh",
            PasswordHash = "irrelevant",
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = "expired-token",
            User = user,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),   // yesterday => expired
            RevokedAt = null                           // not revoked, just old
        });
        await db.SaveChangesAsync();

        var request = new RefreshTokenRequest { RefreshToken = "expired-token" };

        // Act
        Func<Task> act = async () => await sut.RefreshTokenAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    // -----------------------------------------------------------------
    // TEST 6: A revoked refresh token throws UnauthorizedException.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RefreshTokenAsync_RevokedToken_ThrowsUnauthorizedException()
    {
        // Arrange
        var sut = BuildSut(out var db);
        var user = new User
        {
            Email = "refresh@example.com",
            FirstName = "Re",
            LastName = "Fresh",
            PasswordHash = "irrelevant",
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = "revoked-token",
            User = user,
            ExpiresAt = DateTime.UtcNow.AddDays(7),    // still in date...
            RevokedAt = DateTime.UtcNow                // ...but already revoked
        });
        await db.SaveChangesAsync();

        var request = new RefreshTokenRequest { RefreshToken = "revoked-token" };

        // Act
        Func<Task> act = async () => await sut.RefreshTokenAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
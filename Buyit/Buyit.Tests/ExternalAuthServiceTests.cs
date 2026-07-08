using FluentAssertions;                       // .Should() readable assertions
using Microsoft.EntityFrameworkCore;          // DbContextOptionsBuilder, UseInMemoryDatabase, SingleAsync
using Microsoft.Extensions.Options;           // Options.Create(...)
using Moq;                                    // Mock<T>, It.IsAny, Returns
using Xunit;                                  // [Fact]

using Buyit.Application.Common;               // JwtSettings
using Buyit.Application.DTOs;                 // GoogleClaims, AuthResponse
using Buyit.Application.Interfaces;           // IJwtTokenService
using Buyit.Domain.Constants;                 // AuthProviders
using Buyit.Domain.Entities;                  // User, UserExternalLogin
using Buyit.Domain.Enums;                     // UserRole
using Buyit.Domain.Exceptions;                // ValidationException
using Buyit.Infrastructure.Data;              // AppDbContext
using Buyit.Infrastructure.Services;          // ExternalAuthService (the system under test)

namespace Buyit.Tests;

public class ExternalAuthServiceTests
{
    // -----------------------------------------------------------------
    // SHARED SETUP
    // Builds a fresh ExternalAuthService for ONE test, wired to:
    //   - a brand-new in-memory database (no real SQL Server),
    //   - a FAKE token service that returns constant strings,
    //   - minimal JwtSettings (only ExpiryMinutes is read).
    // The 'out db' hands the caller the same in-memory database so a
    // test can seed rows into it before acting.
    // -----------------------------------------------------------------
    private static ExternalAuthService BuildSut(out AppDbContext db)
    {
        // A uniquely-named in-memory store, private to THIS test (isolation).
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

        // Minimal settings; ExternalAuthService only reads ExpiryMinutes.
        var jwtOptions = Options.Create(new JwtSettings { ExpiryMinutes = 15 });

        // Construct the SUT exactly as Program.cs would, but with our fakes.
        return new ExternalAuthService(db, jwtMock.Object, jwtOptions);
    }

    // Convenience: a complete, valid set of Google claims for a test user.
    // Mirrors the four facts Google sends: sub, email, name, picture.
    private static GoogleClaims ValidGoogleClaims() => new()
    {
        Subject = "google-sub-1234567890",       // the permanent Google id ("sub")
        Email = "carl@gmail.com",
        EmailVerified = true,                 // Google confirmed the user owns this email
        Name = "Carl Ibrahim",                // will be split into First="Carl", Last="Ibrahim"
        Picture = "https://example.com/avatar.png"
    };

    // -----------------------------------------------------------------
    // TEST 1: No matching UserExternalLogin exists -> a NEW passwordless
    //         user AND its Google link are created, and tokens returned.
    // -----------------------------------------------------------------
    [Fact]
    public async Task FindOrCreateUserAsync_NewGoogleUser_CreatesUserAndExternalLogin()
    {
        // Arrange: empty database, valid Google claims.
        var sut = BuildSut(out var db);
        var claims = ValidGoogleClaims();

        // Act: run the one method under test.
        AuthResponse result = await sut.FindOrCreateUserAsync(claims);

        // Assert (the returned response):
        result.Should().NotBeNull();
        result.User.Email.Should().Be("carl@gmail.com");
        result.User.FirstName.Should().Be("Carl");
        result.User.LastName.Should().Be("Ibrahim");
        result.User.Role.Should().Be("Customer");
        result.AccessToken.Should().Be("fake-access-token");
        result.RefreshToken.Should().Be("fake-refresh-token");

        // Assert (the database side effects):
        var savedUser = await db.Users.SingleAsync(u => u.Email == "carl@gmail.com");
        savedUser.PasswordHash.Should().BeNull();           // Google-only user has NO password

        var savedLink = await db.UserExternalLogins.SingleAsync();
        savedLink.Provider.Should().Be(AuthProviders.Google);
        savedLink.ProviderUserId.Should().Be("google-sub-1234567890");
        savedLink.UserId.Should().Be(savedUser.Id);          // link points at the new user
    }

    // -----------------------------------------------------------------
    // TEST 2: A UserExternalLogin already exists for (Google, sub) ->
    //         the SAME user is returned and NO duplicate rows are made.
    // -----------------------------------------------------------------
    [Fact]
    public async Task FindOrCreateUserAsync_ExistingGoogleLink_ReturnsSameUser_NoDuplicates()
    {
        // Arrange: pre-seed an existing Google user + their link.
        var sut = BuildSut(out var db);

        var existingUser = new User
        {
            Email = "carl@gmail.com",
            FirstName = "Carl",
            LastName = "Ibrahim",
            PasswordHash = null,                 // they originally signed up via Google
            Role = UserRole.Customer
        };
        db.UserExternalLogins.Add(new UserExternalLogin
        {
            Provider = AuthProviders.Google,
            ProviderUserId = "google-sub-1234567890",   // SAME sub the claims will carry
            User = existingUser                          // navigation -> EF inserts both, wires FK
        });
        await db.SaveChangesAsync();

        var claims = ValidGoogleClaims();        // same sub + email as the seeded user

        // Act
        AuthResponse result = await sut.FindOrCreateUserAsync(claims);

        // Assert: we got the existing user back...
        result.User.Email.Should().Be("carl@gmail.com");
        result.AccessToken.Should().Be("fake-access-token");

        // ...and crucially, NOTHING was duplicated.
        (await db.Users.CountAsync()).Should().Be(1);
        (await db.UserExternalLogins.CountAsync()).Should().Be(1);
    }

    // -----------------------------------------------------------------
    // TEST 3: The email already belongs to a PASSWORD account (no Google
    //         link) AND Google verified the email -> the Google identity
    //         is LINKED to that existing account and tokens are issued
    //         (account linking). No new/duplicate user is created.
    // -----------------------------------------------------------------
    [Fact]
    public async Task FindOrCreateUserAsync_EmailBelongsToPasswordAccount_LinksGoogleAndSignsIn()
    {
        // Arrange: a normal password user with the SAME email, but NO Google link.
        var sut = BuildSut(out var db);

        db.Users.Add(new User
        {
            Email = "carl@gmail.com",
            FirstName = "Carl",
            LastName = "LocalAccount",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123", 12), // a real password user
            Role = UserRole.Customer
        });
        await db.SaveChangesAsync();

        var claims = ValidGoogleClaims();   // same email, Google-verified, first Google login

        // Act
        AuthResponse result = await sut.FindOrCreateUserAsync(claims);

        // Assert: signed into the EXISTING account (not a new one), tokens issued.
        result.User.Email.Should().Be("carl@gmail.com");
        result.User.LastName.Should().Be("LocalAccount");   // proves it's the existing user
        result.AccessToken.Should().Be("fake-access-token");

        // A Google link was created and attached to the existing user; no duplicate user.
        (await db.Users.CountAsync()).Should().Be(1);
        var link = await db.UserExternalLogins.SingleAsync();
        link.Provider.Should().Be(AuthProviders.Google);
        link.ProviderUserId.Should().Be("google-sub-1234567890");
    }

    // -----------------------------------------------------------------
    // TEST 4: A missing/blank 'sub' claim -> ValidationException
    //         (mapped to HTTP 400). Verifies the guard clause.
    // -----------------------------------------------------------------
    [Fact]
    public async Task FindOrCreateUserAsync_MissingSubClaim_ThrowsValidationException()
    {
        // Arrange: valid claims EXCEPT the all-important 'sub' is blank.
        var sut = BuildSut(out _);            // we don't need the db here
        var claims = ValidGoogleClaims();
        claims.Subject = "";                  // missing/invalid sub

        // Act
        Func<Task> act = async () => await sut.FindOrCreateUserAsync(claims);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }
}
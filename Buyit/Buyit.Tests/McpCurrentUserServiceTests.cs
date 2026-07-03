using Buyit.MCP;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Buyit.Tests;

// TB-103: the MCP server now derives the caller's identity from HTTP request headers set by the
// API (X-Buyit-Caller-*), instead of environment variables. These tests lock in that behaviour and,
// critically, that identity FAILS CLOSED (no user / not admin) when the headers are absent or the
// request is off-context — so a missing header can never be read as "trusted admin".
public class McpCurrentUserServiceTests
{
    private const string UserIdHeader = "X-Buyit-Caller-UserId";
    private const string RoleHeader = "X-Buyit-Caller-Role";

    // Builds the service over an HttpContext carrying the given headers (null pairs are skipped).
    private static McpCurrentUserService BuildSut(params (string Key, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        foreach (var (key, value) in headers)
            context.Request.Headers[key] = value;

        var accessor = new HttpContextAccessor { HttpContext = context };
        return new McpCurrentUserService(accessor);
    }

    [Fact]
    public void Identity_WithValidAdminHeaders_IsResolvedAndIsAdmin()
    {
        var sut = BuildSut((UserIdHeader, "42"), (RoleHeader, "Admin"));

        sut.UserId.Should().Be(42);
        sut.Role.Should().Be("Admin");
        sut.IsAuthenticated.Should().BeTrue();
        sut.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public void Identity_WithCustomerRole_IsNotAdmin()
    {
        var sut = BuildSut((UserIdHeader, "7"), (RoleHeader, "Customer"));

        sut.UserId.Should().Be(7);
        sut.Role.Should().Be("Customer");
        sut.IsAuthenticated.Should().BeTrue();
        sut.IsAdmin.Should().BeFalse();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("aDmIn")]
    public void IsAdmin_IsCaseInsensitive(string role)
    {
        var sut = BuildSut((UserIdHeader, "1"), (RoleHeader, role));

        sut.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public void Identity_WithNoHeaders_FailsClosed()
    {
        var sut = BuildSut();

        sut.UserId.Should().BeNull();
        sut.Role.Should().BeNull();
        sut.IsAuthenticated.Should().BeFalse();
        sut.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public void Identity_WithNonNumericUserId_YieldsNullUserId()
    {
        var sut = BuildSut((UserIdHeader, "not-a-number"), (RoleHeader, "Admin"));

        sut.UserId.Should().BeNull();
        sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Identity_WithBlankRole_YieldsNullRoleAndNotAdmin()
    {
        var sut = BuildSut((UserIdHeader, "5"), (RoleHeader, "   "));

        sut.Role.Should().BeNull();
        sut.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public void Identity_WithNoHttpContext_FailsClosed()
    {
        // Off-request (HttpContext == null) must never resolve to a trusted identity.
        var sut = new McpCurrentUserService(new HttpContextAccessor { HttpContext = null });

        sut.UserId.Should().BeNull();
        sut.Role.Should().BeNull();
        sut.IsAuthenticated.Should().BeFalse();
        sut.IsAdmin.Should().BeFalse();
    }
}

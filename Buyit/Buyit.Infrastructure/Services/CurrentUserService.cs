using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Buyit.Infrastructure.Services;

/// <summary>
/// Reads the current request's JWT claims so non-controller code (services) can ask
/// "who is calling?" without depending on controllers. Backed by IHttpContextAccessor.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public int? UserId =>
        int.TryParse(User?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public string? Role => User?.FindFirstValue("role");

    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}

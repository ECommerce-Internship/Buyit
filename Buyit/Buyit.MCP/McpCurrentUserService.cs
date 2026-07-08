using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Buyit.MCP;

/// <summary>
/// Resolves the caller's identity from HTTP request headers set by the API on every MCP
/// call (see McpConnector's AdditionalHeaders). This replaces the previous environment-variable
/// mechanism, which only worked when each caller got a private stdio child process. With the
/// HTTP transport the server is a single shared process, so identity MUST travel per-request
/// (TB-103). Falls back to unauthenticated / least-privilege when the headers are absent, so
/// nothing ever silently runs as admin.
/// </summary>
public class McpCurrentUserService : ICurrentUserService
{
    private const string UserIdHeader = "X-Buyit-Caller-UserId";
    private const string RoleHeader = "X-Buyit-Caller-Role";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public McpCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // The headers of the request currently being handled (null if we're somehow off-request).
    private IHeaderDictionary? Headers => _httpContextAccessor.HttpContext?.Request.Headers;

    public int? UserId =>
        int.TryParse(Headers?[UserIdHeader], out var id) ? id : null;

    public string? Role
    {
        get
        {
            var role = Headers?[RoleHeader].ToString();
            return string.IsNullOrWhiteSpace(role) ? null : role;
        }
    }

    public bool IsAuthenticated => UserId is not null;

    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}

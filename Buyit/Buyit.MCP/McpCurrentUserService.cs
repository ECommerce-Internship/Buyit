using Buyit.Application.Interfaces;

namespace Buyit.MCP;

/// <summary>
/// Resolves the caller's identity from environment variables set by the API when it spawns
/// this MCP process (see McpConnector). This replaces the previous hardcoded-admin identity
/// so tools that consult ICurrentUserService act as the REAL caller (TB-98, AC#4).
/// Falls back to unauthenticated / least-privilege when the variables are absent — e.g. when
/// the server is launched standalone — so nothing silently runs as admin.
/// </summary>
public class McpCurrentUserService : ICurrentUserService
{
    private const string UserIdEnv = "BUYIT_CALLER_USERID";
    private const string RoleEnv = "BUYIT_CALLER_ROLE";

    public int? UserId =>
        int.TryParse(Environment.GetEnvironmentVariable(UserIdEnv), out var id) ? id : null;

    public string? Role
    {
        get
        {
            var role = Environment.GetEnvironmentVariable(RoleEnv);
            return string.IsNullOrWhiteSpace(role) ? null : role;
        }
    }

    public bool IsAuthenticated => UserId is not null;

    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}

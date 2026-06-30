using Buyit.Application.Interfaces;

namespace Buyit.MCP;

public class McpCurrentUserService : ICurrentUserService
{
    public int? UserId => 1;
    public string Role => "Admin";
    public bool IsAuthenticated => true;
    public bool IsAdmin => true;
}
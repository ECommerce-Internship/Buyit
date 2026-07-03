namespace Buyit.Application.Common;

// Strongly-typed view of the "Mcp" section in appsettings.
// Bound once in Program.cs via Configure<McpSettings>(...).
public class McpSettings
{
    // The base URL of the MCP HTTP server, e.g. "http://localhost:5100" locally or
    // "http://buyit-mcp:8080" inside Docker. Read by McpConnector; set per environment
    // in appsettings.json / environment variables so no code change is needed (TB-103).
    public string BaseUrl { get; set; } = string.Empty;

    // Shared secret proving to the MCP server that a request really came from this API.
    // The MCP HTTP endpoint trusts the X-Buyit-Caller-* identity headers ONLY on requests
    // that carry this secret; without it any client reaching the endpoint could spoof an
    // admin identity. Must match the MCP server's Mcp:SharedSecret. Supply via environment
    // variable / user-secrets — never hardcode it in a committed file (TB-103 security fix).
    public string SharedSecret { get; set; } = string.Empty;
}

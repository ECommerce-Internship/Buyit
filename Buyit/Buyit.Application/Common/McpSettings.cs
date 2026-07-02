namespace Buyit.Application.Common;

// Strongly-typed view of the "Mcp" section in appsettings.
// Bound once in Program.cs via Configure<McpSettings>(...).
public class McpSettings
{
    // Path to the Buyit.MCP .csproj file, used to build the default development launch
    // command (`dotnet run --project <ProjectPath>`).
    // Example (relative to the API's working directory): "../Buyit.MCP/Buyit.MCP.csproj"
    public string ProjectPath { get; set; } = string.Empty;

    // The executable that hosts the MCP server. Defaults to the .NET CLI.
    public string Command { get; set; } = "dotnet";

    // Explicit launch arguments. When empty, defaults to `run --project <ProjectPath>`
    // (development). In production — where the SDK and source tree are usually absent —
    // set this to run a prebuilt binary instead, e.g. ["exec", "/app/mcp/Buyit.MCP.dll"].
    public string[] Arguments { get; set; } = Array.Empty<string>();
}
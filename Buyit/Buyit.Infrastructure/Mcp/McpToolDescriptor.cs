using System.Text.Json;

namespace Buyit.Infrastructure.Mcp;

// A protocol-agnostic view of one MCP tool's metadata, decoupled from the
// ModelContextProtocol SDK's McpClientTool (which can't be constructed in tests).
public record McpToolDescriptor(string Name, string? Description, JsonElement JsonSchema);

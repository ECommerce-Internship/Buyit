namespace Buyit.Infrastructure.Mcp;

// A live MCP session: lists the server's tools and runs them. Kept protocol-agnostic
// (no ModelContextProtocol SDK types) so ChatService's tests can mock it directly.
public interface IMcpToolRunner : IAsyncDisposable
{
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken);

    Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}

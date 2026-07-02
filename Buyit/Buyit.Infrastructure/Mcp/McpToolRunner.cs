using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Buyit.Infrastructure.Mcp;

// Adapts a live ModelContextProtocol McpClient to the protocol-agnostic IMcpToolRunner seam.
internal sealed class McpToolRunner : IMcpToolRunner
{
    private readonly McpClient _client;

    public McpToolRunner(McpClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools
            .Select(tool => new McpToolDescriptor(tool.Name, tool.Description, tool.JsonSchema))
            .ToList();
    }

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        // Our tools return a single JSON string in a text content block.
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return text ?? string.Empty;
    }

    public ValueTask DisposeAsync() => ((IAsyncDisposable)_client).DisposeAsync();
}

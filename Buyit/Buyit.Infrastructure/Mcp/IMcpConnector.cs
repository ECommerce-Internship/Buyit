namespace Buyit.Infrastructure.Mcp;

// Launches/connects to the Buyit.MCP server. Kept separate from ChatService so tests
// can substitute a fake runner instead of spawning a real child process.
public interface IMcpConnector
{
    Task<IMcpToolRunner> ConnectAsync(CancellationToken cancellationToken);
}

namespace Buyit.Infrastructure.Mcp;

// Launches/connects to the Buyit.MCP server. Kept separate from ChatService so tests
// can substitute a fake runner instead of spawning a real child process.
public interface IMcpConnector
{
    // The caller's JWT identity is forwarded into the spawned MCP process so that any tool
    // resolving ICurrentUserService sees the REAL caller, not a hardcoded default (TB-98).
    Task<IMcpToolRunner> ConnectAsync(int callerId, string? callerRole, CancellationToken cancellationToken);
}

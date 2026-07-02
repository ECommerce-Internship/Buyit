using Buyit.Application.Common;
using Buyit.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Buyit.Infrastructure.Mcp;

// Launches the Buyit.MCP project as a child process (stdio) and returns a live tool runner.
// The caller MUST dispose the returned runner (use 'await using') to stop the process.
public class McpConnector : IMcpConnector
{
    private readonly McpSettings _mcpSettings;
    private readonly ILogger<McpConnector> _logger;

    public McpConnector(IOptions<McpSettings> mcpOptions, ILogger<McpConnector> logger)
    {
        _mcpSettings = mcpOptions.Value;
        _logger = logger;
    }

    public async Task<IMcpToolRunner> ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Default to the dev launch (`dotnet run --project <csproj>`); production can
            // override Command/Arguments to run a prebuilt binary with no SDK or source tree.
            var arguments = _mcpSettings.Arguments.Length > 0
                ? _mcpSettings.Arguments
                : new[] { "run", "--project", _mcpSettings.ProjectPath };

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Buyit.MCP",
                Command = _mcpSettings.Command,
                Arguments = arguments
            });

            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            return new McpToolRunner(client);
        }
        catch (Exception ex)
        {
            // Any failure to launch/handshake the MCP server is an external-service failure.
            _logger.LogWarning(ex, "Could not connect to the Buyit MCP server.");
            throw new ExternalServiceException("Could not reach the Buyit tools service.", ex);
        }
    }
}

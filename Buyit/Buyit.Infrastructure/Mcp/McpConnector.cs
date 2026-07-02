using System.Globalization;
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

    public async Task<IMcpToolRunner> ConnectAsync(int callerId, string? callerRole, CancellationToken cancellationToken)
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
                Arguments = arguments,
                // Forward the authenticated caller's identity to the child process. InheritEnvironmentVariables
                // defaults to true, so these are layered ON TOP of the inherited env (PATH, connection strings,
                // etc.) — never use null as a value, which would REMOVE a variable. The MCP server's
                // McpCurrentUserService reads these so its tools act as the real caller, not a hardcoded admin.
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["BUYIT_CALLER_USERID"] = callerId.ToString(CultureInfo.InvariantCulture),
                    ["BUYIT_CALLER_ROLE"] = callerRole ?? string.Empty
                }
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

using System.Globalization;
using Buyit.Application.Common;
using Buyit.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Buyit.Infrastructure.Mcp;

// Connects to the long-lived Buyit.MCP HTTP service and returns a live tool runner.
// The caller MUST dispose the returned runner (use 'await using') to close the session.
public class McpConnector : IMcpConnector
{
    // Named HttpClient registered in the API's Program.cs. Using the factory means the
    // underlying socket handler is pooled and reused across chat messages, instead of a
    // fresh HttpClient (and socket pool) per request — which would risk SNAT/port exhaustion.
    public const string HttpClientName = "BuyitMcp";

    private readonly McpSettings _mcpSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpConnector> _logger;

    public McpConnector(
        IOptions<McpSettings> mcpOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<McpConnector> logger)
    {
        _mcpSettings = mcpOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IMcpToolRunner> ConnectAsync(int callerId, string? callerRole, CancellationToken cancellationToken)
    {
        try
        {
            // A pooled client from the factory — cheap to create, handler is reused (see HttpClientName).
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);

            // Connect to the MCP HTTP service instead of spawning a child process (TB-103).
            // The base URL is read from config (Mcp:BaseUrl) so it changes per environment with no
            // code change. Identity travels as per-request HTTP headers — NOT environment variables —
            // because the HTTP server is shared by all callers (see McpCurrentUserService). The
            // shared secret authenticates THIS API to the MCP server so those identity headers are
            // only trusted from us and can't be spoofed by anything else that reaches the endpoint.
            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Name = "Buyit.MCP",
                    Endpoint = new Uri(_mcpSettings.BaseUrl),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string>
                    {
                        ["X-Buyit-Mcp-Secret"] = _mcpSettings.SharedSecret,
                        ["X-Buyit-Caller-UserId"] = callerId.ToString(CultureInfo.InvariantCulture),
                        ["X-Buyit-Caller-Role"] = callerRole ?? string.Empty
                    }
                },
                httpClient,
                ownsHttpClient: false); // the factory owns the client's lifetime, not the transport

            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            return new McpToolRunner(client);
        }
        catch (Exception ex)
        {
            // Any failure to reach/handshake the MCP server is an external-service failure.
            _logger.LogWarning(ex, "Could not connect to the Buyit MCP server at {BaseUrl}.", _mcpSettings.BaseUrl);
            throw new ExternalServiceException("Could not reach the Buyit tools service.", ex);
        }
    }
}

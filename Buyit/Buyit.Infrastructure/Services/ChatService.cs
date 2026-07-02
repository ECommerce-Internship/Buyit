using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Mcp;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

public class ChatService : IChatService
{
    // Hard cap on Gemini<->tool round-trips per message, so the loop always terminates.
    private const int MaxToolRounds = 5;

    // Tools a non-admin (Customer/Seller) may use. Everything not listed here is admin-only.
    // Allowlist, not blocklist: a newly-added tool is denied to customers until explicitly added.
    private static readonly HashSet<string> NonAdminTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "search_products",       // public catalogue search — safe
        "get_product",           // single product details — safe
        "get_customer_orders",   // a user's OWN orders — safe once we inject their id
    };

    // Argument names the model must NEVER control (the CALLER's own identity). We delete these
    // from model output and, where a tool needs identity, set them ourselves from the JWT.
    // Note: 'sellerUserId' is deliberately NOT here — it is a filter argument on the admin-only
    // get_dashboard_summary tool, not the caller's identity. Stripping it would silently force
    // admins' per-store dashboard queries back to platform-wide. If a sellerUserId-taking tool
    // is ever added to NonAdminTools, re-scope it explicitly in ApplyServerSideIdentity.
    private static readonly string[] IdentityArgKeys = { "userId", "isAdmin" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiSettings _geminiSettings;
    private readonly IMcpConnector _mcpConnector;
    private readonly IValidator<ChatRequest> _validator;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiSettings> geminiOptions,
        IMcpConnector mcpConnector,
        IValidator<ChatRequest> validator,
        ICurrentUserService currentUser,
        ILogger<ChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _geminiSettings = geminiOptions.Value;
        _mcpConnector = mcpConnector;
        _validator = validator;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ChatResponse> SendMessageAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // Identity comes ONLY from the JWT. [Authorize] on the controller should guarantee a user,
        // but we fail closed if it's ever missing rather than silently trusting a default.
        var callerId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to use the assistant.");
        var callerRole = _currentUser.Role;

        // New conversations get a fresh id (real cross-request memory arrives in a later ticket).
        var conversation = string.IsNullOrWhiteSpace(request.conversationId)
            ? Guid.NewGuid().ToString()
            : request.conversationId;

        // 1) Connect to the MCP server and learn its tools (Parts 3 & 4).
        //    'await using' guarantees the child process is shut down when we leave this method.
        await using var mcp = await _mcpConnector.ConnectAsync(callerId, callerRole, cancellationToken);
        var allTools = await mcp.ListToolsAsync(cancellationToken);
        var allowedTools = GetToolsForRole(callerRole, allTools);
        var toolsPayload = BuildToolsPayload(allowedTools);

        // 2) Seed the conversation with the user's message.
        var contents = new List<object>
        {
            new { role = "user", parts = new object[] { new { text = request.message } } }
        };

        // Remembers the most recent tool output, surfaced if we hit the round cap.
        var lastToolOutput = string.Empty;

        // 3) The function-calling loop — capped at MaxToolRounds tool round-trips.
        for (var round = 0; round < MaxToolRounds; round++)
        {
            var requestBody = new { contents, tools = toolsPayload };
            var rawBody = await CallGeminiAsync(requestBody, cancellationToken);

            // Parse this round's reply. Anything we keep past this block must be .Clone()d.
            string? textAnswer = null;
            JsonElement functionCall = default;
            bool hasFunctionCall = false;

            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var modelContent = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content");

                foreach (var part in modelContent.GetProperty("parts").EnumerateArray())
                {
                    if (part.TryGetProperty("functionCall", out var fc))
                    {
                        functionCall = fc.Clone();   // keep it alive past the 'using'
                        hasFunctionCall = true;
                        break;                        // handle one tool call per round
                    }

                    if (part.TryGetProperty("text", out var textElement))
                        textAnswer = textElement.GetString();
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                           or IndexOutOfRangeException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Unexpected Gemini envelope shape.");
                throw new ExternalServiceException("The AI service returned an unexpected response.", ex);
            }

            // 3a) No tool requested → Gemini gave its final answer. We're done.
            if (!hasFunctionCall)
            {
                var reply = string.IsNullOrWhiteSpace(textAnswer)
                    ? "Sorry, I couldn't produce an answer."
                    : textAnswer!;
                return new ChatResponse(reply, conversation);
            }

            // 3b) A tool was requested. Echo the model's functionCall turn back into history.
            var toolName = functionCall.GetProperty("name").GetString() ?? string.Empty;

            // Defence in depth. Only reject with 403 when the tool REALLY EXISTS but was filtered
            // out for this role — that is a genuine authorization boundary. A name that exists for
            // nobody is a model hallucination, not a permissions issue, so let it fall through to
            // CallMcpToolAsync and surface as a 502 (unchanged from before).
            var isRealTool = allTools.Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
            var isAllowed = allowedTools.Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
            if (isRealTool && !isAllowed)
            {
                _logger.LogWarning(
                    "Model requested tool '{Tool}' not permitted for role '{Role}'.", toolName, callerRole);
                throw new ForbiddenException($"The tool '{toolName}' is not available to you.");
            }

            var toolArgs = functionCall.TryGetProperty("args", out var a) ? a : default;

            contents.Add(new
            {
                role = "model",
                parts = new object[] { new { functionCall } }
            });

            // 3c) Run the real tool via MCP and add its result as a functionResponse turn.
            var toolOutput = await CallMcpToolAsync(mcp, toolName, toolArgs, callerId, cancellationToken);
            lastToolOutput = toolOutput;

            contents.Add(new
            {
                role = "user",
                parts = new object[]
                {
                    new { functionResponse = new { name = toolName, response = new { result = toolOutput } } }
                }
            });

            // loop again so Gemini can react to the tool output
        }

        // 4) We used all rounds and Gemini still wanted tools. Return what we gathered.
        _logger.LogInformation(
            "Chat loop hit the {Cap}-round tool cap for conversation {Id}.", MaxToolRounds, conversation);
        return new ChatResponse(
            string.IsNullOrWhiteSpace(lastToolOutput)
                ? "I gathered some information but couldn't fully finish. Please try rephrasing."
                : $"I gathered some information but couldn't fully finish. Here's what I found so far: {lastToolOutput}",
            conversation);
    }

    // Rule 2 — role-based tool filtering. Admin sees everything; everyone else gets the
    // safe subset. The model literally cannot request a tool that isn't in this list.
    private static IReadOnlyList<McpToolDescriptor> GetToolsForRole(
        string? role, IReadOnlyList<McpToolDescriptor> allTools)
    {
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            return allTools;

        return allTools
            .Where(tool => NonAdminTools.Contains(tool.Name))
            .ToList();
    }

    // Copies only the schema keywords Gemini understands, recursing into nested schemas.
    // Input: one tool's JSON-Schema (JsonElement). Output: a Gemini-safe parameters object.
    private static Dictionary<string, object?> ToGeminiSchema(JsonElement schema)
    {
        var result = new Dictionary<string, object?>();

        // If the tool has no schema object, advertise an empty object schema.
        if (schema.ValueKind != JsonValueKind.Object)
        {
            result["type"] = "object";
            result["properties"] = new Dictionary<string, object?>();
            return result;
        }

        foreach (var property in schema.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    // .NET's JSON-Schema generator emits nullable types as an array,
                    // e.g. "type": ["string","null"]. Gemini's "type" is a singular enum,
                    // so unwrap the array into a plain type string + "nullable": true.
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var typeNames = property.Value.EnumerateArray()
                            .Select(t => t.GetString())
                            .Where(t => t is not null)
                            .ToList();

                        if (typeNames.Contains("null"))
                            result["nullable"] = true;

                        var nonNullType = typeNames.FirstOrDefault(t => t != "null");
                        if (nonNullType is not null)
                            result["type"] = nonNullType;
                    }
                    else
                    {
                        result["type"] = property.Value.Clone();
                    }
                    break;

                case "format":
                case "description":
                case "nullable":
                    result[property.Name] = property.Value.Clone();
                    break;

                case "enum":
                    result["enum"] = property.Value.Clone();
                    break;

                case "required":
                    result["required"] = property.Value.Clone();
                    break;

                case "items":
                    // An array's element schema — recurse.
                    result["items"] = ToGeminiSchema(property.Value);
                    break;

                case "properties":
                    // Each named property is itself a schema — recurse into each.
                    var cleaned = new Dictionary<string, object?>();
                    foreach (var field in property.Value.EnumerateObject())
                        cleaned[field.Name] = ToGeminiSchema(field.Value);
                    result["properties"] = cleaned;
                    break;

                // Everything else (e.g. $schema, additionalProperties, title, default)
                // is intentionally dropped — Gemini can reject unknown keywords.
            }
        }

        // Gemini requires object schemas to declare "type": "object".
        if (!result.ContainsKey("type") && result.ContainsKey("properties"))
            result["type"] = "object";

        return result;
    }

    // Turns the MCP tool catalogue into Gemini's "tools" payload:
    //   [ { functionDeclarations: [ { name, description, parameters }, ... ] } ]
    private static object[] BuildToolsPayload(IReadOnlyList<McpToolDescriptor> tools)
    {
        var declarations = tools
            .Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = ToGeminiSchema(tool.JsonSchema)
            })
            .ToArray();

        return new object[] { new { functionDeclarations = declarations } };
    }

    // Sends one generateContent request to Gemini and returns the raw JSON body as a string.
    // Mirrors GeminiService's HTTP + error handling. Throws ExternalServiceException on any failure.
    private async Task<string> CallGeminiAsync(object requestBody, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("GeminiClient");
        var url = $"v1beta/models/{_geminiSettings.Model}:generateContent";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody)
        };
        httpRequest.Headers.Add("x-goog-api-key", _geminiSettings.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gemini request timed out.");
            throw new ExternalServiceException("The AI service timed out. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error calling Gemini.");
            throw new ExternalServiceException("Could not reach the AI service.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Gemini returned {StatusCode}. Body: {Body}", (int)status, errorBody);

            var messageText = status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => "The AI service rejected the request (check the API key).",
                HttpStatusCode.TooManyRequests
                    => "The AI service is rate-limited. Please try again shortly.",
                _ => "The AI service returned an error."
            };
            throw new ExternalServiceException(messageText);
        }

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
            throw new ExternalServiceException("The AI service returned an empty response.");

        return rawBody;
    }

    // Runs one MCP tool and returns its text output. Wraps failures as ExternalServiceException.
    private async Task<string> CallMcpToolAsync(
        IMcpToolRunner mcp, string toolName, JsonElement args, int callerId, CancellationToken cancellationToken)
    {
        try
        {
            var rawArgs = ArgsToDictionary(args);
            var arguments = ApplyServerSideIdentity(toolName, rawArgs, callerId);
            return await mcp.CallToolAsync(toolName, arguments, cancellationToken);
        }
        catch (ExternalServiceException)
        {
            throw; // already the right type — don't double-wrap
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP tool '{Tool}' failed.", toolName);
            throw new ExternalServiceException($"The tool '{toolName}' failed to run.", ex);
        }
    }

    // Rule 1 — remove any identity args the model produced, then stamp in the real identity
    // from the JWT for the tools that need it. The model can never widen its own access.
    private IReadOnlyDictionary<string, object?> ApplyServerSideIdentity(
        string toolName, IReadOnlyDictionary<string, object?> modelArgs, int callerId)
    {
        // 1) Start from the model's args but DROP every identity-related key.
        var safeArgs = modelArgs
            .Where(kv => !IdentityArgKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // 2) Stamp in the trusted identity for the tools that require it.
        switch (toolName.ToLowerInvariant())
        {
            case "get_customer_orders":
                // A customer's own order history — always self-scoped to the JWT's user.
                safeArgs["userId"] = callerId;
                break;

            case "get_order":
                // Look-up by order id: pass the real id + real admin flag so OrderService's
                // ownership check (if (!isAdmin && order.UserId != userId) -> Forbidden) is honoured.
                safeArgs["userId"] = callerId;
                safeArgs["isAdmin"] = _currentUser.IsAdmin;
                break;

            // Other tools (search_products, get_product, get_low_stock_products,
            // get_dashboard_summary, generate_product_content) take no per-user identity arg,
            // so there is nothing to inject — the stripping above already removed any the model tried.
        }

        return safeArgs;
    }

    // Converts Gemini's functionCall.args JSON object into the dictionary CallToolAsync wants.
    private static IReadOnlyDictionary<string, object?> ArgsToDictionary(JsonElement args)
    {
        var dictionary = new Dictionary<string, object?>();

        if (args.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in args.EnumerateObject())
                dictionary[property.Name] = property.Value.Clone();
        }

        return dictionary;
    }
}
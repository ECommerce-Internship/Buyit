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

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiSettings _geminiSettings;
    private readonly IMcpConnector _mcpConnector;
    private readonly IValidator<ChatRequest> _validator;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiSettings> geminiOptions,
        IMcpConnector mcpConnector,
        IValidator<ChatRequest> validator,
        ILogger<ChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _geminiSettings = geminiOptions.Value;
        _mcpConnector = mcpConnector;
        _validator = validator;
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

        // New conversations get a fresh id (real cross-request memory arrives in a later ticket).
        var conversation = string.IsNullOrWhiteSpace(request.conversationId)
            ? Guid.NewGuid().ToString()
            : request.conversationId;

        // 1) Connect to the MCP server and learn its tools (Parts 3 & 4).
        //    'await using' guarantees the child process is shut down when we leave this method.
        await using var mcp = await _mcpConnector.ConnectAsync(cancellationToken);
        var tools = await mcp.ListToolsAsync(cancellationToken);
        var toolsPayload = BuildToolsPayload(tools);

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
            var toolArgs = functionCall.TryGetProperty("args", out var a) ? a : default;

            contents.Add(new
            {
                role = "model",
                parts = new object[] { new { functionCall } }
            });

            // 3c) Run the real tool via MCP and add its result as a functionResponse turn.
            var toolOutput = await CallMcpToolAsync(mcp, toolName, toolArgs, cancellationToken);
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
        IMcpToolRunner mcp, string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        try
        {
            var arguments = ArgsToDictionary(args);
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
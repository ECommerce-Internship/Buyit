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

    // System instruction sent on every Gemini call: grounds the model's role and tells it to use
    // the conversation history and act on confirmations. Without this the model has no persona and
    // may ignore context (e.g. answering "yes" as if it were a brand-new request).
    private const string SystemInstruction =
        "You are Buyit's friendly shopping assistant. Always use the conversation history for " +
        "context. When the user confirms a previous suggestion (e.g. replies 'yes'), carry out " +
        "that action using the appropriate tool rather than asking again. Use search_products to " +
        "check the catalogue: a product is IN STOCK when its quantityInStock is greater than 0, " +
        "and only say a product is unavailable after a search actually returns no results. " +
        "You can manage the cart with add_to_cart, update_cart_item, remove_from_cart, clear_cart, " +
        "apply_coupon and remove_coupon, and place an order with checkout. When the user asks how Buyit " +
        "or one of its features works (e.g. checkout, coupons, returns, seller sign-up), use " +
        "search_documentation and answer ONLY from the passages it returns; if they don't cover it, say " +
        "you don't have that information rather than guessing. Checkout needs a full " +
        "shipping address (line 1, city, state, postal code and country); ask the user for any missing " +
        "address fields before calling it. IMPORTANT: always confirm with the user — restating exactly " +
        "what will happen — BEFORE you checkout, clear the cart, or remove an item; adding items or " +
        "applying a coupon may be done directly. Never invent product IDs, prices, coupon codes or " +
        "addresses: rely on tool results or ask the user. After helping, you may briefly suggest one " +
        "relevant next step or related product, but keep it short.";

    // Tools a non-admin (Customer/Seller) may use. Everything not listed here is admin-only.
    // Allowlist, not blocklist: a newly-added tool is denied to customers until explicitly added.
    private static readonly HashSet<string> NonAdminTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "search_products",       // public catalogue search — safe
        "get_product",           // single product details — safe
        "search_documentation",  // RAG over Buyit's own feature docs — public, read-only

        "get_customer_orders",   // legacy self-scoped orders (kept; get_my_orders is preferred)
        "get_my_orders",         // TB-100: self-scoped order history (no userId param)
        "add_to_cart",           // TB-100: adds to the caller's own cart
        "view_cart",             // TB-100: reads the caller's own cart
        "update_cart_item",      // change quantity of an item in the caller's own cart
        "remove_from_cart",      // remove one item from the caller's own cart
        "clear_cart",            // empty the caller's own cart
        "apply_coupon",          // apply a discount code to the caller's own cart
        "remove_coupon",         // remove the coupon from the caller's own cart
        "checkout",              // place an order from the caller's own cart (self-scoped)
    };

    // Extra tools a Seller may use ON TOP OF the shopper tools above — each read-only and
    // scoped to the seller's OWN store. A seller is still a shopper, so their effective set is
    // NonAdminTools ∪ SellerTools (see GetToolsForRole). Store scope is enforced server-side:
    // 'sellerUserId'-taking tools are forced to the caller's id in ApplyServerSideIdentity, and
    // get_my_store_orders self-scopes inside the MCP tool from the JWT identity.
    private static readonly HashSet<string> SellerTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_dashboard_summary",   // own-store revenue/orders/growth (sellerUserId forced to caller)
        "get_top_products",        // own-store best sellers (sellerUserId forced to caller)
        "get_low_stock_products",  // own-store low-stock list (sellerUserId forced to caller)
        "get_my_store_orders",     // orders against the caller's own store(s) (self-scoped in MCP)
    };

    // Store-scoped tools whose 'sellerUserId' filter must be pinned to the caller when a Seller
    // asks — the model must never widen it to another store or platform-wide. Admins are NOT
    // pinned (they keep the free filter — null = platform-wide, or any seller id).
    private static readonly HashSet<string> SellerScopedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_dashboard_summary", "get_top_products", "get_low_stock_products",
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
    private readonly IConversationStore _conversationStore;
    private readonly ChatHistorySettings _historySettings;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiSettings> geminiOptions,
        IMcpConnector mcpConnector,
        IValidator<ChatRequest> validator,
        ICurrentUserService currentUser,
        IConversationStore conversationStore,
        IOptions<ChatHistorySettings> historyOptions,
        ILogger<ChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _geminiSettings = geminiOptions.Value;
        _mcpConnector = mcpConnector;
        _validator = validator;
        _currentUser = currentUser;
        _conversationStore = conversationStore;
        _historySettings = historyOptions.Value;
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

        // New conversations get a fresh id; an existing id continues that conversation's memory.
        var conversation = string.IsNullOrWhiteSpace(request.conversationId)
            ? Guid.NewGuid().ToString()
            : request.conversationId;

        // Load this user's prior turns for this conversation (empty if new / Redis down). Scoped by
        // callerId, so another user's conversationId can never resolve to this user's history.
        var history = await _conversationStore.GetAsync(conversation, callerId, cancellationToken);

        // The whole tool + AI pipeline is wrapped so that a transient MCP/Gemini failure still
        // records the user's turn (paired with an apology to keep the user/model alternation
        // intact), preserving conversation context for the next message. SaveAsync fails open,
        // so this is best-effort, and we rethrow so the middleware still returns the real error.
        try
        {
            // 1) Connect to the MCP server and learn its tools (Parts 3 & 4).
            //    'await using' closes the pooled HTTP session to the MCP HTTP service when we leave
            //    this method (HTTP transport, TB-103 — there is no child process).
            await using var mcp = await _mcpConnector.ConnectAsync(callerId, callerRole, cancellationToken);
            var allTools = await mcp.ListToolsAsync(cancellationToken);
            var allowedTools = GetToolsForRole(callerRole, allTools);
            var toolsPayload = BuildToolsPayload(allowedTools);

            // A seller may only see their OWN store. If they ask about another store we refuse
            // outright (below) rather than silently rescoping — otherwise the model narrates the
            // seller's own data as if it were the other store's, which is misleading.
            var callerIsSeller = string.Equals(callerRole, "Seller", StringComparison.OrdinalIgnoreCase);

            // 2) Seed the conversation: prior turns first (so Gemini has context), then the new message.
            var contents = new List<object>();
            foreach (var turn in history)
                contents.Add(new { role = turn.Role, parts = new object[] { new { text = turn.Text } } });
            contents.Add(new { role = "user", parts = new object[] { new { text = request.message } } });

            // Remembers the most recent tool output, surfaced if we hit the round cap.
            var lastToolOutput = string.Empty;

            // 3) The function-calling loop — capped at MaxToolRounds tool round-trips.
            for (var round = 0; round < MaxToolRounds; round++)
            {
                // system_instruction grounds the model's role and tells it to use the history (see
                // SystemInstruction). Sent on every round so it applies to tool follow-ups too.
                var requestBody = new
                {
                    system_instruction = new { parts = new object[] { new { text = SystemInstruction } } },
                    contents,
                    tools = toolsPayload
                };
                var rawBody = await CallGeminiAsync(requestBody, cancellationToken);

                // Parse this round's reply. Anything we keep past this block must be .Clone()d.
                string? textAnswer = null;
                JsonElement functionCall = default;
                bool hasFunctionCall = false;
                // The model's turn EXACTLY as Gemini returned it (role + parts). We echo this back
                // verbatim below instead of rebuilding it, because Gemini 3.x attaches an opaque
                // "thoughtSignature" to functionCall parts and REJECTS the next round if it isn't
                // sent back. Cloning the whole content preserves the signature (and any thinking
                // parts); on 2.x models there's simply no signature and this still works.
                JsonElement modelTurn = default;

                try
                {
                    using var doc = JsonDocument.Parse(rawBody);
                    var modelContent = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content");
                    modelTurn = modelContent.Clone();   // keep it alive past the 'using'

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
                    await PersistTurnAsync(conversation, callerId, history, request.message, reply, cancellationToken);
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

                // A seller naming a store that isn't their own → privacy refusal, before any data is
                // fetched. (ApplyServerSideIdentity still pins sellerUserId to the caller as a
                // security backstop, so nothing leaks even if this check is ever bypassed.)
                if (callerIsSeller
                    && SellerScopedTools.Contains(toolName)
                    && SellerNamedForeignStore(toolArgs, callerId))
                {
                    _logger.LogWarning(
                        "Seller {Caller} asked for another store's data via '{Tool}' — refused.", callerId, toolName);
                    const string refusal = "Sorry, I can't provide this information due to privacy reasons.";
                    await PersistTurnAsync(conversation, callerId, history, request.message, refusal, cancellationToken);
                    return new ChatResponse(refusal, conversation);
                }

                // Echo the model's tool-call turn back verbatim (incl. Gemini 3.x thoughtSignature).
                contents.Add(modelTurn);

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
            var cappedReply = string.IsNullOrWhiteSpace(lastToolOutput)
                ? "I gathered some information but couldn't fully finish. Please try rephrasing."
                : $"I gathered some information but couldn't fully finish. Here's what I found so far: {lastToolOutput}";
            await PersistTurnAsync(conversation, callerId, history, request.message, cappedReply, cancellationToken);
            return new ChatResponse(cappedReply, conversation);
        }
        catch (Exception ex) when (ex is ExternalServiceException or ForbiddenException)
        {
            await PersistTurnAsync(conversation, callerId, history, request.message,
                "Sorry, I ran into a problem handling that. Please try again.", cancellationToken);
            throw;
        }
    }

    // Appends this exchange to the history, trims to the last N turns (windowing), and saves.
    private async Task PersistTurnAsync(
        string conversationId, int userId, List<ConversationTurn> history,
        string userMessage, string modelReply, CancellationToken cancellationToken)
    {
        history.Add(new ConversationTurn("user", userMessage));
        history.Add(new ConversationTurn("model", modelReply));

        // Windowing: keep only the most recent MaxTurns so Gemini's context stays bounded. Remove an
        // EVEN number of turns so the window never starts with a dangling "model" turn (Gemini
        // requires the first content to be a "user" turn) — matters when MaxTurns is odd.
        if (history.Count > _historySettings.MaxTurns)
        {
            var toRemove = history.Count - _historySettings.MaxTurns;
            if (toRemove % 2 != 0) toRemove++;
            history.RemoveRange(0, toRemove);
        }

        await _conversationStore.SaveAsync(conversationId, userId, history, cancellationToken);
    }

    // Rule 2 — role-based tool filtering. Admin sees everything; a Seller gets the shopper subset
    // PLUS the store-scoped seller tools; a Customer gets only the shopper subset. The model
    // literally cannot request a tool that isn't in the returned list.
    private static IReadOnlyList<McpToolDescriptor> GetToolsForRole(
        string? role, IReadOnlyList<McpToolDescriptor> allTools)
    {
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            return allTools;

        var allowed = NonAdminTools;
        if (string.Equals(role, "Seller", StringComparison.OrdinalIgnoreCase))
            allowed = new HashSet<string>(NonAdminTools.Concat(SellerTools), StringComparer.OrdinalIgnoreCase);

        return allTools
            .Where(tool => allowed.Contains(tool.Name))
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

            // Other tools (search_products, get_product, get_my_orders, get_my_store_orders,
            // generate_product_content) take no per-user identity arg here — get_my_store_orders
            // self-scopes inside the MCP tool from the JWT, and the rest are public/self-scoped.
        }

        // (helper SellerNamedForeignStore, below, decides the privacy refusal from these same args.)

        // Store-scoped filter tools: when a SELLER asks, pin 'sellerUserId' to their own id so the
        // model can never read another store or go platform-wide. Admins keep the free filter, so
        // we leave their (possibly null) value untouched. Runs after the switch so it also catches
        // any 'sellerUserId' the model tried to smuggle in for a seller.
        if (SellerScopedTools.Contains(toolName)
            && string.Equals(_currentUser.Role, "Seller", StringComparison.OrdinalIgnoreCase))
        {
            safeArgs["sellerUserId"] = callerId;
        }

        return safeArgs;
    }

    // True when a seller's tool call names a store OTHER than their own — i.e. the model produced a
    // 'sellerUserId' that is present and not the caller's id. Absent, null, or equal to the caller
    // all mean "their own store" (allowed); anything else is a foreign store → privacy refusal.
    private static bool SellerNamedForeignStore(JsonElement args, int callerId)
    {
        if (args.ValueKind != JsonValueKind.Object) return false;
        if (!args.TryGetProperty("sellerUserId", out var value)) return false;
        if (value.ValueKind == JsonValueKind.Null) return false;

        // A clean integer we can compare; any other shape (string, etc.) is treated as foreign.
        return !(value.ValueKind == JsonValueKind.Number
                 && value.TryGetInt32(out var id)
                 && id == callerId);
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
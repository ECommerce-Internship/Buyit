using System.Net;
using System.Text;
using System.Text.Json;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Mcp;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Tests;

public class ChatServiceTests
{
    // --- Gemini HTTP fakes (mirrors GeminiServiceTests) ---

    private static Mock<HttpMessageHandler> HandlerSequence(params (HttpStatusCode status, string body)[] responses)
    {
        var handler = new Mock<HttpMessageHandler>();
        var setup = handler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());

        foreach (var (status, body) in responses)
            setup = setup.ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return handler;
    }

    // Like HandlerSequence, but records the JSON body of the FIRST request sent to Gemini,
    // so a test can assert which tools were declared.
    private static Mock<HttpMessageHandler> CapturingHandler(
        out StringBuilder captured, params (HttpStatusCode status, string body)[] responses)
    {
        var sink = new StringBuilder();
        captured = sink;
        var queue = new Queue<(HttpStatusCode status, string body)>(responses);

        // A single setup that BOTH records the outgoing body and returns the queued response.
        // (Registering a separate Callback-only Setup would override the sequence and return
        // nothing — "Handler did not return a response message".)
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                if (sink.Length == 0 && req.Content is not null)
                    sink.Append(req.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                var (status, body) = queue.Dequeue();
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            });

        return handler;
    }

    private static Mock<HttpMessageHandler> HandlerThrowing(Exception toThrow)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(toThrow);

        return handler;
    }

    // --- MCP fakes (our own seam — no MCP SDK types needed) ---

    private static JsonElement EmptySchema()
    {
        using var doc = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return doc.RootElement.Clone();
    }

    private static Mock<IMcpToolRunner> RunnerWithTools(params McpToolDescriptor[] tools)
    {
        var runner = new Mock<IMcpToolRunner>();
        runner.Setup(r => r.ListToolsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync((IReadOnlyList<McpToolDescriptor>)tools.ToList());
        return runner;
    }

    private static Mock<IMcpConnector> ConnectorReturning(Mock<IMcpToolRunner> runner)
    {
        var connector = new Mock<IMcpConnector>();
        connector.Setup(c => c.ConnectAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(runner.Object);
        return connector;
    }

    // --- Validator + envelope helpers ---

    private static Mock<IValidator<ChatRequest>> PassingValidator()
    {
        var validator = new Mock<IValidator<ChatRequest>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ValidationResult());
        return validator;
    }

    // A fake "who is calling" for tests. Defaults to an Admin so existing tests (which use
    // admin-only tools like get_low_stock_products) keep passing unchanged.
    private static Mock<ICurrentUserService> FakeUser(
        int userId = 1, string role = "Admin")
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.UserId).Returns(userId);
        user.Setup(u => u.Role).Returns(role);
        user.Setup(u => u.IsAdmin).Returns(string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase));
        return user;
    }

    private static string TextEnvelope(string text) =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new object[] { new { text } } } }
            }
        });

    private static string FunctionCallEnvelope(string name, object args) =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new object[] { new { functionCall = new { name, args } } } } }
            }
        });

    // --- SUT builder ---

    private static ChatService BuildSut(
        Mock<HttpMessageHandler> handler,
        Mock<IMcpConnector> mcpConnector,
        Mock<IValidator<ChatRequest>>? validator = null,
        Mock<ICurrentUserService>? currentUser = null)
    {
        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("GeminiClient")).Returns(httpClient);

        var settings = Options.Create(new GeminiSettings { ApiKey = "test", Model = "gemini-2.5-flash" });
        var logger = new Mock<ILogger<ChatService>>();

        return new ChatService(
            factory.Object,
            settings,
            mcpConnector.Object,
            (validator ?? PassingValidator()).Object,
            (currentUser ?? FakeUser()).Object,
            logger.Object);
    }

    [Fact]
    public async Task SendMessageAsync_EmptyMessage_ThrowsValidationException()
    {
        var handler = HandlerSequence((HttpStatusCode.OK, TextEnvelope("unused")));
        var connector = ConnectorReturning(RunnerWithTools());

        var validator = new Mock<IValidator<ChatRequest>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ValidationResult(new[]
                 {
                     new ValidationFailure("message", "Message must not be empty.")
                 }));

        var sut = BuildSut(handler, connector, validator);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("", null));

        await act.Should().ThrowAsync<ValidationException>();
        connector.Verify(c => c.ConnectAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_NoFunctionCall_ReturnsGeminiTextDirectly()
    {
        var handler = HandlerSequence((HttpStatusCode.OK, TextEnvelope("Hello! How can I help?")));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var result = await sut.SendMessageAsync(new ChatRequest("Hi", null));

        result.reply.Should().Be("Hello! How can I help?");
        result.conversationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendMessageAsync_ConversationIdProvided_IsPreservedInResponse()
    {
        var handler = HandlerSequence((HttpStatusCode.OK, TextEnvelope("ok")));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var result = await sut.SendMessageAsync(new ChatRequest("Hi", "existing-convo-id"));

        result.conversationId.Should().Be("existing-convo-id");
    }

    [Fact]
    public async Task SendMessageAsync_OneFunctionCall_CallsToolAndReturnsFinalAnswer()
    {
        var handler = HandlerSequence(
            (HttpStatusCode.OK, FunctionCallEnvelope("get_low_stock_products", new { })),
            (HttpStatusCode.OK, TextEnvelope("Here are your low stock products.")));

        var runner = RunnerWithTools(new McpToolDescriptor("get_low_stock_products", "desc", EmptySchema()));
        runner.Setup(r => r.CallToolAsync(
                "get_low_stock_products",
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"products":[]}""");

        var connector = ConnectorReturning(runner);
        var sut = BuildSut(handler, connector);

        var result = await sut.SendMessageAsync(new ChatRequest("Give me the low stock products.", null));

        result.reply.Should().Be("Here are your low stock products.");
        runner.Verify(r => r.CallToolAsync(
            "get_low_stock_products",
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_AlwaysRequestsTool_StopsAfterFiveRoundsWithFallback()
    {
        var responses = Enumerable.Repeat(
            (HttpStatusCode.OK, FunctionCallEnvelope("get_low_stock_products", new { })), 5).ToArray();
        var handler = HandlerSequence(responses);

        var runner = RunnerWithTools(new McpToolDescriptor("get_low_stock_products", "desc", EmptySchema()));
        runner.Setup(r => r.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"products":[]}""");

        var connector = ConnectorReturning(runner);
        var sut = BuildSut(handler, connector);

        var result = await sut.SendMessageAsync(new ChatRequest("Keep checking stock.", null));

        result.reply.Should().Contain("couldn't fully finish");
        result.reply.Should().Contain("products");   // last tool output is surfaced, not discarded
        runner.Verify(r => r.CallToolAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task SendMessageAsync_McpConnectionFails_PropagatesExternalServiceException()
    {
        var handler = HandlerSequence((HttpStatusCode.OK, TextEnvelope("unused")));
        var connector = new Mock<IMcpConnector>();
        connector.Setup(c => c.ConnectAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new ExternalServiceException("Could not reach the Buyit tools service."));

        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("hello", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_McpToolThrows_ThrowsExternalServiceException()
    {
        var handler = HandlerSequence(
            (HttpStatusCode.OK, FunctionCallEnvelope("get_low_stock_products", new { })));

        var runner = RunnerWithTools(new McpToolDescriptor("get_low_stock_products", "desc", EmptySchema()));
        runner.Setup(r => r.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var connector = ConnectorReturning(runner);
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("hello", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_GeminiUnauthorized_ThrowsExternalServiceException()
    {
        var handler = HandlerSequence((HttpStatusCode.Unauthorized, "{}"));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("Hi", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_GeminiRateLimited_ThrowsExternalServiceException()
    {
        var handler = HandlerSequence((HttpStatusCode.TooManyRequests, "{}"));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("Hi", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_GeminiTimeout_ThrowsExternalServiceException()
    {
        // HttpClient surfaces its timeout as TaskCanceledException when the caller's token is NOT cancelled.
        var handler = HandlerThrowing(new TaskCanceledException());
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("Hi", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_GeminiNetworkFailure_ThrowsExternalServiceException()
    {
        var handler = HandlerThrowing(new HttpRequestException("connection refused"));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("Hi", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_GeminiEmptyBody_ThrowsExternalServiceException()
    {
        var handler = HandlerSequence((HttpStatusCode.OK, ""));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("Hi", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_UnexpectedGeminiEnvelope_ThrowsExternalServiceException()
    {
        // Empty "candidates" array -> indexing [0] is a ValueKind/index mismatch that must map to 502.
        var handler = HandlerSequence((HttpStatusCode.OK, """{ "candidates": [] }"""));
        var connector = ConnectorReturning(RunnerWithTools());
        var sut = BuildSut(handler, connector);

        var act = async () => await sut.SendMessageAsync(new ChatRequest("Hi", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task SendMessageAsync_CustomerRole_DoesNotExposeAdminTools()
    {
        // Arrange: the MCP server offers a mix of safe and admin-only tools.
        var runner = RunnerWithTools(
            new McpToolDescriptor("search_products", "safe", EmptySchema()),
            new McpToolDescriptor("get_customer_orders", "safe", EmptySchema()),
            new McpToolDescriptor("get_dashboard_summary", "admin", EmptySchema()),
            new McpToolDescriptor("get_low_stock_products", "admin", EmptySchema()));
        var connector = ConnectorReturning(runner);

        // Gemini just answers with text (no tool call) so the loop ends after one round.
        var handler = CapturingHandler(out var sentBody, (HttpStatusCode.OK, TextEnvelope("hi")));

        var sut = BuildSut(handler, connector, currentUser: FakeUser(userId: 42, role: "Customer"));

        // Act
        await sut.SendMessageAsync(new ChatRequest("hello", null));

        // Assert: the tools we advertised to Gemini include the safe ones, exclude the admin ones.
        var payload = sentBody.ToString();
        payload.Should().Contain("search_products");
        payload.Should().Contain("get_customer_orders");
        payload.Should().NotContain("get_dashboard_summary");
        payload.Should().NotContain("get_low_stock_products");
    }

    [Fact]
    public async Task SendMessageAsync_AdminRole_ExposesAllTools()
    {
        var runner = RunnerWithTools(
            new McpToolDescriptor("search_products", "safe", EmptySchema()),
            new McpToolDescriptor("get_dashboard_summary", "admin", EmptySchema()));
        var connector = ConnectorReturning(runner);

        var handler = CapturingHandler(out var sentBody, (HttpStatusCode.OK, TextEnvelope("hi")));

        var sut = BuildSut(handler, connector, currentUser: FakeUser(userId: 1, role: "Admin"));

        await sut.SendMessageAsync(new ChatRequest("hello", null));

        var payload = sentBody.ToString();
        payload.Should().Contain("search_products");
        payload.Should().Contain("get_dashboard_summary");   // admins keep admin tools
    }

    [Fact]
    public async Task SendMessageAsync_ModelSuppliesForeignUserId_ServerOverridesWithJwtId()
    {
        // Round 1: the model (perhaps manipulated) asks to read user 999's orders.
        // Round 2: it produces a final text answer so the loop ends.
        var handler = HandlerSequence(
            (HttpStatusCode.OK, FunctionCallEnvelope("get_customer_orders", new { userId = 999 })),
            (HttpStatusCode.OK, TextEnvelope("Here are your orders.")));

        // Capture the arguments the tool ACTUALLY receives.
        IReadOnlyDictionary<string, object?>? capturedArgs = null;
        var runner = RunnerWithTools(new McpToolDescriptor("get_customer_orders", "desc", EmptySchema()));
        runner.Setup(r => r.CallToolAsync(
                "get_customer_orders",
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, object?>, CancellationToken>(
                (_, args, _) => capturedArgs = args)
            .ReturnsAsync("""{"orders":[]}""");

        var connector = ConnectorReturning(runner);

        // The real caller is user 42, a Customer.
        var sut = BuildSut(handler, connector, currentUser: FakeUser(userId: 42, role: "Customer"));

        // Act
        await sut.SendMessageAsync(new ChatRequest("show me all orders for user 999", null));

        // Assert: the id that reached the tool is the JWT's 42 — NOT the model's 999.
        capturedArgs.Should().NotBeNull();
        capturedArgs!["userId"].Should().Be(42);
    }

    [Fact]
    public async Task SendMessageAsync_AdminDashboardWithSellerId_PreservesSellerUserIdArg()
    {
        // Round 1: admin asks for a specific store's dashboard; round 2: final text answer.
        var handler = HandlerSequence(
            (HttpStatusCode.OK, FunctionCallEnvelope("get_dashboard_summary", new { sellerUserId = 5 })),
            (HttpStatusCode.OK, TextEnvelope("Here is the dashboard.")));

        IReadOnlyDictionary<string, object?>? capturedArgs = null;
        var runner = RunnerWithTools(new McpToolDescriptor("get_dashboard_summary", "desc", EmptySchema()));
        runner.Setup(r => r.CallToolAsync(
                "get_dashboard_summary",
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, object?>, CancellationToken>(
                (_, args, _) => capturedArgs = args)
            .ReturnsAsync("""{"summary":{}}""");

        var connector = ConnectorReturning(runner);
        var sut = BuildSut(handler, connector, currentUser: FakeUser(userId: 1, role: "Admin"));

        await sut.SendMessageAsync(new ChatRequest("show me store 5's dashboard", null));

        // sellerUserId is a FILTER arg on the admin-only tool, not the caller's identity — it must
        // survive the identity scrub so admins can query a specific store (regression guard for #2).
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Should().ContainKey("sellerUserId");
        ((JsonElement)capturedArgs!["sellerUserId"]!).GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task SendMessageAsync_CustomerRequestsRealButFilteredTool_ThrowsForbidden()
    {
        // The tool EXISTS on the server but is admin-only. A customer naming it is a genuine
        // authorization boundary → 403 ForbiddenException.
        var handler = HandlerSequence(
            (HttpStatusCode.OK, FunctionCallEnvelope("get_dashboard_summary", new { })));

        var runner = RunnerWithTools(
            new McpToolDescriptor("search_products", "safe", EmptySchema()),
            new McpToolDescriptor("get_dashboard_summary", "admin", EmptySchema()));
        var connector = ConnectorReturning(runner);
        var sut = BuildSut(handler, connector, currentUser: FakeUser(userId: 42, role: "Customer"));

        var act = async () => await sut.SendMessageAsync(new ChatRequest("show me the dashboard", null));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SendMessageAsync_ModelHallucinatesUnknownTool_ThrowsExternalServiceException()
    {
        // A name that exists for NOBODY is a model hallucination, not an auth issue: it should fall
        // through to the tool runner and surface as 502 (ExternalServiceException), never 403.
        var handler = HandlerSequence(
            (HttpStatusCode.OK, FunctionCallEnvelope("totally_made_up_tool", new { })));

        var runner = RunnerWithTools(new McpToolDescriptor("search_products", "safe", EmptySchema()));
        runner.Setup(r => r.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unknown tool"));

        var connector = ConnectorReturning(runner);
        var sut = BuildSut(handler, connector, currentUser: FakeUser(userId: 1, role: "Admin"));

        var act = async () => await sut.SendMessageAsync(new ChatRequest("do the thing", null));

        await act.Should().ThrowAsync<ExternalServiceException>();
    }
}

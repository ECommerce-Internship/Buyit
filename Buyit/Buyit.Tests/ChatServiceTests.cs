using System.Net;
using System.Text;
using System.Text.Json;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
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
        connector.Setup(c => c.ConnectAsync(It.IsAny<CancellationToken>()))
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
        Mock<IValidator<ChatRequest>>? validator = null)
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
        connector.Verify(c => c.ConnectAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        connector.Setup(c => c.ConnectAsync(It.IsAny<CancellationToken>()))
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
}

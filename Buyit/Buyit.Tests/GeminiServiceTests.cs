using System.Net;
using System.Text;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Domain.Exceptions;
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

public class GeminiServiceTests
{
    // Builds a GeminiService whose HttpClient is backed by a fake handler returning
    // the given status + body.
    private static GeminiService BuildSut(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return BuildSutFromHandler(handler);
    }

    // Builds a GeminiService whose HttpClient throws the given exception when sending
    // (used to simulate timeouts and network failures).
    private static GeminiService BuildSutThrowing(Exception toThrow)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(toThrow);

        return BuildSutFromHandler(handler);
    }

    private static GeminiService BuildSutFromHandler(Mock<HttpMessageHandler> handler)
    {
        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("GeminiClient")).Returns(httpClient);

        var settings = Options.Create(new GeminiSettings { ApiKey = "test", Model = "gemini-2.5-flash" });

        // Validator passes by default (input rules tested elsewhere). Token-agnostic so
        // it matches regardless of the CancellationToken the service forwards.
        var validator = new Mock<IValidator<GenerateProductContentRequest>>();
        validator.Setup(v => v.ValidateAsync(
                    It.IsAny<GenerateProductContentRequest>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ValidationResult());

        var logger = new Mock<ILogger<GeminiService>>();

        return new GeminiService(factory.Object, settings, validator.Object, logger.Object);
    }

    // Wraps inner content JSON inside Gemini's envelope shape.
    private static string Envelope(string innerJson)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(innerJson); // produces a quoted, escaped string
        return $$"""
           { "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }
           """;
    }

    private static Task<ProductContentResponse> Act(GeminiService sut) =>
        sut.GenerateProductContentAsync("Case", "Accessories", "silicone");

    [Fact]
    public async Task GenerateProductContentAsync_ValidResponse_ReturnsContent()
    {
        // Arrange
        var inner = """
           {
             "description": "A great case.",
             "features": ["a","b","c","d","e"],
             "seoTitle": "Great Case",
             "metaDescription": "Buy this great case today."
           }
           """;
        var sut = BuildSut(HttpStatusCode.OK, Envelope(inner));

        // Act
        var result = await Act(sut);

        // Assert
        result.Features.Should().HaveCount(5);
        result.Description.Should().NotBeEmpty();
        result.SeoTitle.Should().Be("Great Case");
        result.MetaDescription.Should().Be("Buy this great case today.");
    }

    [Fact]
    public async Task GenerateProductContentAsync_RateLimited_ThrowsExternalServiceException()
    {
        var sut = BuildSut(HttpStatusCode.TooManyRequests, "{}");

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_Unauthorized_ThrowsExternalServiceException()
    {
        var sut = BuildSut(HttpStatusCode.Unauthorized, "{}");

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_Timeout_ThrowsExternalServiceException()
    {
        // HttpClient surfaces its timeout as TaskCanceledException when the caller's token is NOT cancelled.
        var sut = BuildSutThrowing(new TaskCanceledException());

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_NetworkFailure_ThrowsExternalServiceException()
    {
        var sut = BuildSutThrowing(new HttpRequestException("connection refused"));

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_EmptyBody_ThrowsExternalServiceException()
    {
        var sut = BuildSut(HttpStatusCode.OK, "");

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_NullCandidates_ThrowsExternalServiceException()
    {
        // "candidates": null is a ValueKind mismatch -> JsonElement throws InvalidOperationException,
        // which must be caught and mapped to 502 (regression guard for the catch filter).
        var sut = BuildSut(HttpStatusCode.OK, """{ "candidates": null }""");

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_MissingContentNode_ThrowsExternalServiceException()
    {
        // A blocked candidate (e.g. finishReason SAFETY) has no "content" node.
        var sut = BuildSut(HttpStatusCode.OK, """{ "candidates": [ { "finishReason": "SAFETY" } ] }""");

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_InvalidJson_ThrowsExternalServiceException()
    {
        var sut = BuildSut(HttpStatusCode.OK, Envelope("this is not json"));

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_NullParsedContent_ThrowsExternalServiceException()
    {
        // The generated text is literally JSON null -> deserializes to a null DTO.
        var sut = BuildSut(HttpStatusCode.OK, Envelope("null"));

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_WrongFeatureCount_ThrowsExternalServiceException()
    {
        var inner = """
           { "description":"x", "features":["a","b"], "seoTitle":"t", "metaDescription":"m" }
           """;
        var sut = BuildSut(HttpStatusCode.OK, Envelope(inner));

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_SeoTitleTooLong_ThrowsExternalServiceException()
    {
        var longTitle = new string('a', 61);
        var inner = $$"""
           { "description":"x", "features":["a","b","c","d","e"], "seoTitle":"{{longTitle}}", "metaDescription":"m" }
           """;
        var sut = BuildSut(HttpStatusCode.OK, Envelope(inner));

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task GenerateProductContentAsync_MetaDescriptionTooLong_ThrowsExternalServiceException()
    {
        var longMeta = new string('a', 156);
        var inner = $$"""
           { "description":"x", "features":["a","b","c","d","e"], "seoTitle":"t", "metaDescription":"{{longMeta}}" }
           """;
        var sut = BuildSut(HttpStatusCode.OK, Envelope(inner));

        var act = async () => await Act(sut);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }
}

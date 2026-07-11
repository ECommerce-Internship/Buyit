using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buyit.Application.Common;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buyit.Infrastructure.Services;

// TB-156: calls Gemini's embedContent endpoint and returns a 768-float vector.
// Mirrors GeminiService's HTTP/error-handling pattern exactly (same GeminiClient, same
// x-goog-api-key header, same ExternalServiceException mapping) so behaviour is consistent.
public class EmbeddingService : IEmbeddingService
{
    // text-embedding-004 always returns a 768-dimension vector; our column is vector(768).
    private const int ExpectedDimensions = 768;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiSettings _settings;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiSettings> settingsOptions,
        ILogger<EmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settingsOptions.Value;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(
        string text,
        EmbeddingTaskType taskType = EmbeddingTaskType.RetrievalDocument,
        CancellationToken cancellationToken = default)
    {
        // 0) Guard: never send an empty body to the API — that's a wasted call that 400s.
        if (string.IsNullOrWhiteSpace(text))
            throw new ExternalServiceException("Cannot embed empty text.");

        // 1) Build the embedContent request body (note the "models/" prefix on the model name).
        //    gemini-embedding-001 defaults to 3072-dim output; pin it to ExpectedDimensions (768)
        //    so the vector matches our vector(768) column. (text-embedding-004 was natively 768 but
        //    is no longer available on this API key — ListModels confirmed only gemini-embedding-*.)
        //    taskType makes retrieval ASYMMETRIC (documents vs queries), which sharply improves the
        //    separation between relevant and irrelevant products versus the untyped default.
        var taskTypeValue = taskType == EmbeddingTaskType.RetrievalQuery
            ? "RETRIEVAL_QUERY"
            : "RETRIEVAL_DOCUMENT";
        var requestBody = new
        {
            model = $"models/{_settings.EmbeddingModel}",
            content = new { parts = new[] { new { text } } },
            taskType = taskTypeValue,
            outputDimensionality = ExpectedDimensions
        };

        // 2) Reuse the SAME named client as the content feature (base address + timeout + pool).
        var client = _httpClientFactory.CreateClient("GeminiClient");
        var url = $"v1beta/models/{_settings.EmbeddingModel}:embedContent";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody)
        };
        httpRequest.Headers.Add("x-goog-api-key", _settings.ApiKey);

        // 3) Send, mapping timeout/network faults to 502 (identical to GeminiService).
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {   // The token was NOT cancelled by the caller, so this is the HttpClient timeout.
            _logger.LogWarning(ex, "Embedding request timed out.");
            throw new ExternalServiceException("The AI embedding service timed out. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error calling the embedding service.");
            throw new ExternalServiceException("Could not reach the AI embedding service.", ex);
        }

        // 4) Status handling: 401/403 -> key problem, 429 -> rate-limited, else generic. All -> 502.
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            // Log the upstream body for operators (may contain quota ids) — never returned to callers.
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Embedding API returned {StatusCode}. Body: {Body}", (int)status, errorBody);

            var message = status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => "The AI embedding service rejected the request (check the API key).",
                HttpStatusCode.TooManyRequests
                    => "The AI embedding service is rate-limited. Please try again shortly.",
                _ => "The AI embedding service returned an error."
            };
            throw new ExternalServiceException(message);
        }

        // 5) Parse embedding.values out of the JSON envelope.
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
            throw new ExternalServiceException("The AI embedding service returned an empty response.");

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var values = doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values");

            var vector = new float[values.GetArrayLength()];
            int i = 0;
            foreach (var v in values.EnumerateArray())
                vector[i++] = v.GetSingle();

            // 6) Sanity-check the dimension: our column is vector(768). A wrong length would
            //    throw deep inside EF on save; catching it here gives a clean 502 instead.
            if (vector.Length != ExpectedDimensions)
                throw new ExternalServiceException(
                    $"The AI embedding service returned {vector.Length} dimensions; expected {ExpectedDimensions}.");

            return vector;
        }
        // InvalidOperationException covers wrong-typed nodes (e.g. "embedding" is null), which
        // JsonElement throws on a ValueKind mismatch.
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                       or IndexOutOfRangeException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Unexpected embedding envelope shape.");
            throw new ExternalServiceException("The AI embedding service returned an unexpected response.", ex);
        }
    }
}

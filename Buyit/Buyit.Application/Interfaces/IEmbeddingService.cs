namespace Buyit.Application.Interfaces;

// TB-156: turns arbitrary text into a semantic embedding vector via Gemini text-embedding-004.
// Throws ExternalServiceException (502) if the embedding API fails or returns a malformed vector.
public interface IEmbeddingService
{
    // Returns a 768-element embedding for the given text. Never returns null on success.
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

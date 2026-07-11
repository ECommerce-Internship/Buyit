namespace Buyit.Application.Interfaces;

// The retrieval ROLE of the text being embedded. Gemini's embedding model produces ASYMMETRIC
// vectors: stored catalogue text must be embedded as a DOCUMENT and the user's search text as a
// QUERY. Matching these correctly is what pulls genuinely-relevant products close to a query and
// pushes unrelated ones away — without it, everything scores ~0.5 and ranking is poor.
public enum EmbeddingTaskType
{
    // Text stored for later retrieval (a product's name/description/category).
    RetrievalDocument,

    // A user's search query, matched against RetrievalDocument vectors.
    RetrievalQuery
}

// TB-156: turns arbitrary text into a semantic embedding vector via Gemini gemini-embedding-001.
// Throws ExternalServiceException (502) if the embedding API fails or returns a malformed vector.
public interface IEmbeddingService
{
    // Returns a 768-element embedding for the given text. `taskType` tells Gemini whether this text
    // is a stored document or a search query (asymmetric retrieval). Never returns null on success.
    Task<float[]> EmbedAsync(
        string text,
        EmbeddingTaskType taskType = EmbeddingTaskType.RetrievalDocument,
        CancellationToken cancellationToken = default);
}

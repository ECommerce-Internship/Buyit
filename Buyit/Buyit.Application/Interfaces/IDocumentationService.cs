using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// RAG over Buyit's own feature documentation. IngestAsync builds the vector corpus from the bundled
// Markdown feature files; SearchAsync retrieves the passages closest to a user's question so the
// chatbot can ground an accurate answer in them. Both embed via IEmbeddingService and rank in
// Postgres with pgvector, mirroring the catalogue's semantic search.
public interface IDocumentationService
{
    // Rebuild the documentation corpus: read every bundled feature Markdown file, split it into
    // chunks, embed each chunk (RetrievalDocument) and store them. Idempotent — replaces the whole
    // corpus each run. Throws ExternalServiceException (502) if the embedding API fails.
    Task<IngestDocsResponse> IngestAsync(CancellationToken cancellationToken = default);

    // Retrieve the `topK` documentation chunks whose meaning is closest to `query`, ranked by cosine
    // distance and filtered by a relevance cutoff. Empty query -> ValidationException; embedding
    // failure -> ExternalServiceException. Returns an empty list when nothing clears the cutoff.
    Task<IReadOnlyList<DocChunkResult>> SearchAsync(
        string query, int topK = 4, CancellationToken cancellationToken = default);
}

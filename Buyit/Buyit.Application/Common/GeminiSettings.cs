namespace Buyit.Application.Common;

// Strongly-typed view of the "Gemini" section in appsettings.
// Bound once in Program.cs via Configure<GeminiSettings>(...).
public class GeminiSettings
{
    // The secret key from Google AI Studio. Comes from appsettings.Development.json
    // locally and from environment variables / Azure config in deployed environments.
    public string ApiKey { get; set; } = string.Empty;

    // Which Gemini model to call, e.g. "gemini-2.5-flash".
    public string Model { get; set; } = "gemini-2.5-flash";

    // TB-156: which embedding model to call. Kept separate from Model (the content model)
    // because they are different models with different output shapes. gemini-embedding-001 is the
    // current GA embedding model (text-embedding-004 is retired); EmbeddingService pins its output
    // to 768 dimensions via outputDimensionality to match the vector(768) column.
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    // Relevance cutoff for semantic search: a product is only returned if its cosine DISTANCE to
    // the query (pgvector "<=>", 0 = identical meaning … ~1+ = unrelated) is <= this value.
    // Without it every embedded product comes back, just re-ranked, so vague queries surface the
    // whole catalogue. Tune per catalogue/model — observed "relevant" hits sit around 0.30–0.48 and
    // clearly-off ones above ~0.55. A value of 0 or less disables the cutoff (rank-only behaviour).
    public double SemanticMaxDistance { get; set; } = 0.5;
}
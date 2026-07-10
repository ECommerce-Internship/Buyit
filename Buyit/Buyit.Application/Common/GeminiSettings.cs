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
}
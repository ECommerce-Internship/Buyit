namespace Buyit.Application.Common;

// Strongly-typed view of the "Documentation" section in appsettings. Bound in Program.cs via
// Configure<DocumentationSettings>(...). Governs the RAG documentation corpus (see IDocumentationService).
public class DocumentationSettings
{
    // Folder holding the feature Markdown files, relative to the app's base directory (they are
    // copied there from Buyit.Infrastructure/Documentation at build). An absolute path is used as-is.
    public string DocsPath { get; set; } = "Documentation";

    // Default number of chunks retrieved per question. Small on purpose: a few tight passages ground
    // the model better than many loose ones and keep the Gemini prompt cheap.
    public int TopK { get; set; } = 4;

    // Relevance cutoff for retrieval: a chunk is only returned if its cosine DISTANCE to the question
    // (pgvector "<=>", 0 = identical … ~1+ = unrelated) is <= this value. Keeps an off-topic question
    // ("what's the weather") from dragging in unrelated docs. A value <= 0 disables the cutoff.
    public double MaxDistance { get; set; } = 0.6;
}

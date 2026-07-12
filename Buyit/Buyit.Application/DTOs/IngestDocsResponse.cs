namespace Buyit.Application.DTOs;

// Result of a documentation ingest run (read the feature Markdown files, chunk, embed, store).
//   Files     — feature files discovered and processed.
//   Chunks    — chunks written to the store across all files.
//   Embedded  — chunks successfully embedded (Chunks - Embedded were left pending after an AI failure).
// Ingest is idempotent — it rebuilds the corpus each run — so it is safe to re-run.
public record IngestDocsResponse(int Files, int Chunks, int Embedded);

namespace Buyit.Application.DTOs;

// One retrieved documentation passage plus how close it was to the question. Distance is the pgvector
// cosine distance (0 = identical meaning … ~1+ = unrelated); smaller is more relevant. Returned by
// IDocumentationService.SearchAsync and handed to Gemini as grounding for a RAG answer.
public record DocChunkResult(string Source, string Heading, string Content, double Distance);

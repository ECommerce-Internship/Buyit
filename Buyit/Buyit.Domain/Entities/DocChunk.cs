using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace Buyit.Domain.Entities;

/// <summary>
/// A retrievable slice of Buyit's own feature documentation, used by the RAG assistant. Each row is
/// one chunk of a Markdown feature file (typically one "##" section), stored alongside its semantic
/// <see cref="Embedding"/> so the chatbot can retrieve the passages closest to a user's question and
/// ground its answer in them. This is the documentation-corpus analogue of the catalogue embedding
/// on <see cref="Product"/> — same pgvector column, same cosine-distance retrieval.
/// </summary>
public class DocChunk
{
    public int Id { get; set; }

    // Which feature file this chunk came from (e.g. "orders.md"). Lets a re-ingest replace a single
    // file's chunks, and lets the assistant cite where an answer came from.
    [Required, MaxLength(200)]
    public string Source { get; set; } = string.Empty;

    // The section heading this chunk was extracted from (the "## ..." line), or the document title
    // for the intro chunk. Prepended to Content when embedding so the vector carries section context.
    [MaxLength(300)]
    public string Heading { get; set; } = string.Empty;

    // The chunk's text — what gets embedded and what is handed back to Gemini as grounding.
    [Required, MaxLength(8000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Semantic embedding of (Heading + Content), 768 dims from Gemini, stored as a Postgres pgvector
    // column and compared with cosine distance. Nullable only transiently between insert and embed;
    // a chunk with no embedding is skipped by retrieval.
    public Vector? Embedding { get; set; }
}

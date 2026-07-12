using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;   // CosineDistance() -> Postgres "<=>" translation

namespace Buyit.Infrastructure.Services;

// RAG over Buyit's own feature documentation. Ingest reads the bundled Markdown feature files, splits
// each into section chunks, embeds them, and stores them as pgvector rows; Search embeds the user's
// question and returns the closest chunks. Deliberately mirrors ProductService's embedding paths
// (same IEmbeddingService, same EmbeddingTaskType asymmetry, same cosine-distance ranking in Postgres).
public class DocumentationService : IDocumentationService
{
    // Guardrails so one chunk can't blow past the vector(768) embedding input or the DocChunk.Content
    // column (8000). Sections longer than this are hard-split into windows before embedding.
    private const int MaxChunkChars = 4000;

    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly DocumentationSettings _settings;
    private readonly ILogger<DocumentationService> _logger;

    public DocumentationService(
        AppDbContext db,
        IEmbeddingService embeddings,
        IOptions<DocumentationSettings> settings,
        ILogger<DocumentationService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IngestDocsResponse> IngestAsync(CancellationToken cancellationToken = default)
    {
        // 1) Resolve the docs folder (absolute path used as-is; relative is under the app base dir,
        //    where the .md files are copied at build). A missing folder is an operator error, not a
        //    downstream failure — surface it clearly.
        var dir = Path.IsPathRooted(_settings.DocsPath)
            ? _settings.DocsPath
            : Path.Combine(AppContext.BaseDirectory, _settings.DocsPath);

        if (!Directory.Exists(dir))
            throw new NotFoundException($"Documentation folder '{dir}' was not found.");

        var files = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        // 2) Rebuild the corpus from scratch so ingest is idempotent — re-running always yields exactly
        //    what's on disk now, with no stale or duplicated chunks. The corpus is small (a handful of
        //    files), so a full rebuild is cheaper and simpler than diffing.
        var existing = await _db.Set<DocChunk>().ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _db.Set<DocChunk>().RemoveRange(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var chunks = new List<DocChunk>();
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var source = Path.GetFileName(file);
            foreach (var (heading, content) in ChunkMarkdown(text))
                chunks.Add(new DocChunk { Source = source, Heading = heading, Content = content });
        }

        // 3) Persist the chunks first (so their text is stored even if embedding is rate-limited), then
        //    embed each one. A single failure is logged and skipped — the chunk stays retrievable-once-
        //    embedded on a later re-run — rather than aborting the whole ingest.
        _db.Set<DocChunk>().AddRange(chunks);
        await _db.SaveChangesAsync(cancellationToken);

        int embedded = 0;
        foreach (var chunk in chunks)
        {
            try
            {
                var input = $"{chunk.Heading}\n{chunk.Content}";
                chunk.Embedding = new Vector(
                    await _embeddings.EmbedAsync(input, EmbeddingTaskType.RetrievalDocument, cancellationToken));
                await _db.SaveChangesAsync(cancellationToken);
                embedded++;
                await Task.Delay(200, cancellationToken);   // gentle throttle to stay under the rate limit
            }
            catch (ExternalServiceException ex)
            {
                _logger.LogWarning(ex, "Ingest: failed to embed chunk from {Source}; will retry on re-run.", chunk.Source);
            }
        }

        _logger.LogInformation(
            "Documentation ingest: {Files} file(s), {Chunks} chunk(s), {Embedded} embedded.",
            files.Length, chunks.Count, embedded);
        return new IngestDocsResponse(files.Length, chunks.Count, embedded);
    }

    public async Task<IReadOnlyList<DocChunkResult>> SearchAsync(
        string query, int topK = 4, CancellationToken cancellationToken = default)
    {
        // 1) Guard: an empty question can't be embedded and would 400 upstream. Mirror ProductService.
        if (string.IsNullOrWhiteSpace(query))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["query"] = new[] { "A question is required." }
            });
        topK = Math.Clamp(topK, 1, 20);

        // 2) Embed the QUESTION as a RETRIEVAL_QUERY so it matches chunks embedded as documents. A
        //    failure here is fatal for retrieval (no fallback) -> propagates as 502.
        var queryVector = new Vector(
            await _embeddings.EmbedAsync(query, EmbeddingTaskType.RetrievalQuery, cancellationToken));

        // 3) Rank in the DATABASE by cosine distance (<=>). Only chunks that actually have an embedding
        //    can rank. Distance is projected ONCE and reused by the ordering, so Postgres computes
        //    'embedding <=> query' a single time per row.
        var maxDistance = _settings.MaxDistance;
        var hits = await _db.Set<DocChunk>()
            .Where(c => c.Embedding != null)
            .Select(c => new
            {
                c.Source,
                c.Heading,
                c.Content,
                Distance = c.Embedding!.CosineDistance(queryVector)
            })
            // Relevance cutoff: drop chunks too far from the question so an off-topic query doesn't drag
            // in unrelated docs. A configured value <= 0 disables the cutoff (rank-only).
            .Where(x => maxDistance <= 0 || x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Take(topK)
            .ToListAsync(cancellationToken);

        return hits
            .Select(h => new DocChunkResult(h.Source, h.Heading, h.Content, h.Distance))
            .ToList();
    }

    // Splits a Markdown feature file into (heading, content) chunks: everything before the first "## "
    // becomes an intro chunk under the document's "# " title, then each "## " section is its own chunk.
    // Very long sections are hard-split into <= MaxChunkChars windows so no single chunk overflows the
    // embedding input or the Content column. Returns nothing for whitespace-only sections.
    private static IEnumerable<(string Heading, string Content)> ChunkMarkdown(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        string docTitle = "";
        string currentHeading = "";
        var buffer = new List<string>();

        // Emits the buffered section (possibly window-split), qualifying the heading with the doc title
        // so each chunk's embedding carries which feature it belongs to.
        IEnumerable<(string, string)> Flush()
        {
            var body = string.Join("\n", buffer).Trim();
            buffer.Clear();
            if (body.Length == 0)
                yield break;

            var qualified = string.IsNullOrEmpty(currentHeading)
                ? docTitle
                : (string.IsNullOrEmpty(docTitle) ? currentHeading : $"{docTitle} — {currentHeading}");

            foreach (var window in SplitToWindows(body, MaxChunkChars))
                yield return (qualified, window);
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                // Document title. Anything buffered before it belongs to the previous section.
                foreach (var chunk in Flush()) yield return chunk;
                docTitle = line[2..].Trim();
                currentHeading = "";
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                foreach (var chunk in Flush()) yield return chunk;
                currentHeading = line[3..].Trim();
            }
            else
            {
                buffer.Add(line);
            }
        }

        foreach (var chunk in Flush()) yield return chunk;
    }

    // Hard-splits an over-long section on paragraph boundaries into windows of at most maxChars,
    // falling back to a raw slice if a single paragraph is itself too long.
    private static IEnumerable<string> SplitToWindows(string body, int maxChars)
    {
        if (body.Length <= maxChars)
        {
            yield return body;
            yield break;
        }

        var paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var current = new System.Text.StringBuilder();

        foreach (var para in paragraphs)
        {
            if (para.Length > maxChars)
            {
                if (current.Length > 0) { yield return current.ToString().Trim(); current.Clear(); }
                for (int i = 0; i < para.Length; i += maxChars)
                    yield return para.Substring(i, Math.Min(maxChars, para.Length - i));
                continue;
            }

            if (current.Length + para.Length + 2 > maxChars && current.Length > 0)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }
            current.Append(para).Append("\n\n");
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }
}

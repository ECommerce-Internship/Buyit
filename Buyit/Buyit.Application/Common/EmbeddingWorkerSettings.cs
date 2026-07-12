namespace Buyit.Application.Common;

// TB-156: configuration for the background worker that keeps product embeddings up to date.
// Bound from the "EmbeddingWorker" configuration section; all values have safe defaults so the
// worker runs sensibly even when the section is absent.
public class EmbeddingWorkerSettings
{
    // Master switch. Set false to disable the self-healing sweep entirely.
    public bool Enabled { get; set; } = true;

    // How long to wait between sweeps once the catalogue is fully embedded.
    public int PollIntervalSeconds { get; set; } = 300;   // 5 minutes

    // How many products to embed per Gemini batch. Kept modest to stay under rate limits;
    // BackfillEmbeddingsAsync clamps this to its own maximum.
    public int BatchSize { get; set; } = 50;

    // Grace period after startup before the first sweep, so migrations/seeding settle first.
    public int StartupDelaySeconds { get; set; } = 20;
}

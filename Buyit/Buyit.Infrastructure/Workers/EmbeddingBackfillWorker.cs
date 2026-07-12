using Buyit.Application.Common;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buyit.Infrastructure.Workers;

// TB-156: self-healing background service that keeps product embeddings complete.
//
// Product creation/update already embeds on the write path (ProductService), but that call is
// best-effort — a transient Gemini failure leaves the row's embedding null and nothing retries it.
// This worker periodically sweeps every product still missing an embedding and generates it, which
// covers ALL sources of null embeddings with no manual step:
//   - new products whose create-time embed failed transiently,
//   - seeded products (DbInitializer inserts rows directly, bypassing the write path),
//   - SFTP-imported products that failed to embed at import time.
//
// It reuses IProductService.BackfillEmbeddingsAsync (the same bounded, throttled, best-effort logic
// behind the admin backfill endpoint) so there is a single embedding code path.
public class EmbeddingBackfillWorker : BackgroundService
{
    private readonly EmbeddingWorkerSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmbeddingBackfillWorker> _logger;

    public EmbeddingBackfillWorker(
        IOptions<EmbeddingWorkerSettings> settings,
        IServiceScopeFactory scopeFactory,
        ILogger<EmbeddingBackfillWorker> logger)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmbeddingBackfillWorker started.");

        if (!_settings.Enabled)
        {
            _logger.LogInformation("EmbeddingBackfillWorker: disabled via configuration. Worker will not run.");
            return;
        }

        // Let startup migrations/seeding settle before the first sweep.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.StartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // shutting down
            }
            catch (Exception ex)
            {
                // Never let a sweep failure kill the worker — log and try again next interval.
                _logger.LogError(ex, "EmbeddingBackfillWorker: sweep failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("EmbeddingBackfillWorker stopped.");
    }

    // Drains pending embeddings in bounded batches until none remain or a batch makes no progress
    // (e.g. Gemini is down), in which case we stop and let the next interval retry.
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        // BackgroundService is a singleton, so resolve the scoped IProductService in a fresh scope.
        using var scope = _scopeFactory.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await products.BackfillEmbeddingsAsync(_settings.BatchSize, force: false, stoppingToken);

            if (result.Embedded > 0)
            {
                _logger.LogInformation(
                    "EmbeddingBackfillWorker: embedded {Embedded} product(s), {Remaining} still pending.",
                    result.Embedded, result.Remaining);
            }

            // Done — everything is embedded.
            if (result.Remaining == 0)
                break;

            // No forward progress this pass (all remaining products failed, e.g. Gemini outage or a
            // rate limit). Stop draining now and let the next scheduled interval retry them.
            if (result.Embedded == 0)
            {
                _logger.LogWarning(
                    "EmbeddingBackfillWorker: {Remaining} product(s) still unembedded but none succeeded this pass; retrying next interval.",
                    result.Remaining);
                break;
            }
        }
    }
}

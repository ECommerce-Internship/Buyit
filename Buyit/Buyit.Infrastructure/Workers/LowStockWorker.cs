using System.Text.Json;
using Azure.Storage.Queues;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buyit.Infrastructure.Workers;

// Background service that polls the low-stock-alerts queue every 30 seconds
// and sends an email for each message via IEmailService
public class LowStockWorker : BackgroundService
{
    private readonly AzureQueueSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LowStockWorker> _logger;
    private const int MaxMessagesPerBatch = 10; // Azure Queue max is 32

    public LowStockWorker(
        IOptions<AzureQueueSettings> settings,
        IServiceScopeFactory scopeFactory,
        ILogger<LowStockWorker> logger)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LowStockWorker started.");

        // If no Azure connection string configured, skip the worker entirely
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            _logger.LogWarning("LowStockWorker: Azure Queue connection string not configured. Worker will not run.");
            return;
        }

        var client = new QueueClient(_settings.ConnectionString, _settings.LowStockQueueName);
        await client.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveMessagesAsync(maxMessages: MaxMessagesPerBatch, cancellationToken: stoppingToken);

                foreach (var queueMessage in response.Value)
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<LowStockMessage>(queueMessage.MessageText);
                        if (message is null)
                        {
                            _logger.LogWarning("LowStockWorker: Received null/unreadable message, deleting.");
                            await client.DeleteMessageAsync(queueMessage.MessageId, queueMessage.PopReceipt, stoppingToken);
                            continue;
                        }

                        _logger.LogInformation(
                            "LowStockWorker: Processing alert for Product '{ProductName}' (ID: {ProductId})",
                            message.ProductName, message.ProductId);

                        // BackgroundService is a singleton — it outlives request scopes.
                        // We must create a fresh scope per message to safely resolve scoped services (IEmailService).
                        using var scope = _scopeFactory.CreateScope();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        await emailService.SendLowStockAlertAsync(message);

                        // Delete the message after successful processing
                        await client.DeleteMessageAsync(queueMessage.MessageId, queueMessage.PopReceipt, stoppingToken);

                        _logger.LogInformation(
                            "LowStockWorker: Alert processed and deleted for Product '{ProductName}'",
                            message.ProductName);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't delete — message will become visible again after visibility timeout
                        _logger.LogError(ex,
                            "LowStockWorker: Failed to process message {MessageId}",
                            queueMessage.MessageId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LowStockWorker: Error polling queue.");
            }

            // Wait 30 seconds before next poll
            await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("LowStockWorker stopped.");
    }
}
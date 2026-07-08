using System.Text.Json;
using Azure.Storage.Queues;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buyit.Infrastructure.Services;

// Serializes LowStockMessage to JSON and sends it to the Azure Storage Queue
public class LowStockAlertService : ILowStockAlertService
{
    private readonly AzureQueueSettings _settings;
    private readonly ILogger<LowStockAlertService> _logger;

    public LowStockAlertService(
        IOptions<AzureQueueSettings> settings,
        ILogger<LowStockAlertService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task TriggerAlertAsync(int productId, string productName, int quantity, int threshold)
    {
        var message = new LowStockMessage(productId, productName, quantity, threshold, DateTime.UtcNow);

        // If no Azure connection string configured, just log and return (local dev fallback)
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            _logger.LogWarning(
                "[DEV] Low stock alert (queue not configured): Product '{ProductName}' (ID: {ProductId}) has {Quantity} units remaining.",
                productName, productId, quantity);
            return;
        }

        try
        {
            var client = new QueueClient(_settings.ConnectionString, _settings.LowStockQueueName);

            // CreateIfNotExists ensures the queue exists before sending
            await client.CreateIfNotExistsAsync();

            var json = JsonSerializer.Serialize(message);
            await client.SendMessageAsync(json);

            _logger.LogInformation(
                "Low stock alert queued for Product '{ProductName}' (ID: {ProductId}), Quantity: {Quantity}",
                productName, productId, quantity);
        }
        catch (Exception ex)
        {
            // Fail-open: a queue outage must never break the stock update
            _logger.LogError(ex,
                "Failed to queue low stock alert for Product '{ProductName}' (ID: {ProductId})",
                productName, productId);
        }
    }
}
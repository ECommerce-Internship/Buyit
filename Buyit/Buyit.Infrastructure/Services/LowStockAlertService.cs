using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buyit.Infrastructure.Services;

// Placeholder implementation — real alert logic (email/Azure) wired in the Azure epic
public class LowStockAlertService : ILowStockAlertService
{
    private readonly ILogger<LowStockAlertService> _logger;

    public LowStockAlertService(ILogger<LowStockAlertService> logger)
    {
        _logger = logger;
    }

    public Task TriggerAlertAsync(int productId, string productName, int quantity)
    {
        _logger.LogWarning("Low stock alert: Product '{ProductName}' (ID: {ProductId}) has {Quantity} units remaining.",
            productName, productId, quantity);

        return Task.CompletedTask;
    }
}
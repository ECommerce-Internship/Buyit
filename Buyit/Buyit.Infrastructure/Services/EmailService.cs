using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buyit.Infrastructure.Services;

// Placeholder email service — logs until SendGrid is configured
public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public Task SendOrderConfirmationAsync(int orderId, string userEmail, decimal totalAmount)
    {
        _logger.LogInformation(
            "Order confirmation email queued for Order #{OrderId} to {Email}, total: {Total}",
            orderId, userEmail, totalAmount);

        return Task.CompletedTask;
    }

    public Task SendLowStockAlertAsync(LowStockMessage message)
    {
        _logger.LogWarning(
            "Low stock alert email (not sent — SendGrid not configured): Product '{ProductName}' (ID: {ProductId}), Quantity: {Quantity}",
            message.ProductName, message.ProductId, message.Quantity);

        return Task.CompletedTask;
    }
}
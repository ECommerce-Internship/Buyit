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

    public Task SendLowStockAlertAsync(LowStockMessage message, string recipientEmail)
    {
        _logger.LogWarning(
            "Low stock alert email (not sent — SendGrid not configured): Product '{ProductName}' (ID: {ProductId}), Quantity: {Quantity}, would go to {Email}",
            message.ProductName, message.ProductId, message.Quantity, recipientEmail);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(string recipientEmail, string code)
    {
        _logger.LogWarning(
            "Password reset code email (not sent — SendGrid not configured): would go to {Email}",
            recipientEmail);
        return Task.CompletedTask;
    }
}
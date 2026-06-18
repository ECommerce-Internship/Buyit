using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buyit.Infrastructure.Services;

// Placeholder email service and logs confirmation details until real sending is wired in the Azure 
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
}
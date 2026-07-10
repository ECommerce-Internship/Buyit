using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Defines order confirmation email sending 
public interface IEmailService
{
    Task SendOrderConfirmationAsync(int orderId, string userEmail, decimal totalAmount);
    Task SendLowStockAlertAsync(LowStockMessage message, string recipientEmail);
    Task SendPasswordResetCodeAsync(string recipientEmail, string code);
}
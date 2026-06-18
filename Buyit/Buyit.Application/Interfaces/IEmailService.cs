namespace Buyit.Application.Interfaces;

// Defines order confirmation email sending 
public interface IEmailService
{

    Task SendOrderConfirmationAsync(int orderId, string userEmail, decimal totalAmount);
}
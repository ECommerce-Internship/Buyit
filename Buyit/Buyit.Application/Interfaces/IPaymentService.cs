using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Defines the payment operations for customers and admins.
public interface IPaymentService
{
    // Customer: pay for one of their own orders.
    Task<PaymentResponse> ProcessPaymentAsync(int userId, ProcessPaymentRequest request);

    // Customer/Admin: fetch the payment for an order (ownership enforced unless admin).
    Task<PaymentResponse> GetByOrderIdAsync(int orderId, int userId, bool isAdmin);

    // Admin: refund a paid payment (sets payment Refunded, order Cancelled).
    Task<PaymentResponse> RefundAsync(int paymentId);

    // Admin: paginated list of all payments, optionally filtered by status.
    Task<PaginatedResult<PaymentResponse>> GetAllPaymentsAsync(int page, int pageSize, string? status);
}
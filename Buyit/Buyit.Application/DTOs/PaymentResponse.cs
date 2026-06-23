namespace Buyit.Application.DTOs;

// What the API returns to describe a payment (used by all 4 endpoints).
public record PaymentResponse(
    int PaymentId,
    int OrderId,
    decimal Amount,
    string Method,
    string Status,
    string? TransactionId,
    DateTime? PaidAt
);
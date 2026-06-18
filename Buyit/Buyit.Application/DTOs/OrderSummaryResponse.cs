namespace Buyit.Application.DTOs;

// Lightweight summary used in paginated lists — does not include full order items
public record OrderSummaryResponse(
    int OrderId,
    DateTime OrderDate,
    string Status,
    decimal TotalAmount,
    int ItemCount,
    string? PaymentStatus
);
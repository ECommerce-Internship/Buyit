namespace Buyit.Application.DTOs;

// Lightweight summary used in paginated lists — does not include full line items.
public record OrderSummaryResponse(
    int OrderId,
    string CustomerEmail,
    DateTime OrderDate,
    string Status,
    decimal TotalAmount,
    int StoreOrderCount,
    int ItemCount,
    string? PaymentStatus
);

namespace Buyit.Application.DTOs;

// Represents the full cart state, including items, applied coupon, and computed totals.
public record CartResponse(
    int CartId,
    IEnumerable<CartItemResponse> Items,
    decimal Subtotal,
    string? CouponCode,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal FinalTotal
);
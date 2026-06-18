namespace Buyit.Application.DTOs;

// Represents a single product line within an order, including the price snapshot at purchase time.
public record OrderItemResponse(
    int OrderItemId,
    int ProductId,
    string ProductName,
    string Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal
);
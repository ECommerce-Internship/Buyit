namespace Buyit.Application.DTOs;

// Represents a single product line in the cart, including computed line total.
public record CartItemResponse(
    int CartItemId,
    int ProductId,
    string ProductName,
    string Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    int StoreId,
    string StoreName,
    int QuantityInStock
);
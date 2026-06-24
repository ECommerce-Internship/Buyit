// Represents a placed order returned to the client. In the marketplace model the order
// fans out into per-store sub-orders (StoreOrders), so line items live under each StoreOrder.
namespace Buyit.Application.DTOs;

public record OrderResponse(
    int OrderId,
    DateTime OrderDate,
    string Status,             // rolled-up from the StoreOrders
    decimal TotalAmount,
    decimal DiscountAmount,
    string ShippingLine1,
    string? ShippingLine2,
    string ShippingCity,
    string ShippingPostalCode,
    string ShippingCountry,
    string? PaymentStatus,
    IEnumerable<StoreOrderResponse> StoreOrders
);

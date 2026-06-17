using Buyit.Domain.Enums;

// Represents a placed order returned to the client, including shipping details and line items
namespace Buyit.Application.DTOs;

public record OrderResponse(
    int OrderId,
    DateTime OrderDate,
    string Status,
    decimal TotalAmount,
    string ShippingLine1,
    string? ShippingLine2,
    string ShippingCity,
    string ShippingPostalCode,
    string ShippingCountry,
    IEnumerable<OrderItemResponse> Items
);
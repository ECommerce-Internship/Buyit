namespace Buyit.Application.DTOs;

// Request body for placing an order, containing the customer's shipping address.
public record PlaceOrderRequest(
    string ShippingLine1,
    string? ShippingLine2,
    string ShippingCity,
    string ShippingPostalCode,
    string ShippingCountry
);
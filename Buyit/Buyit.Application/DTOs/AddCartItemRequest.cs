namespace Buyit.Application.DTOs;

// Request body for adding a product to the cart.
public record AddCartItemRequest(int ProductId, int Quantity);
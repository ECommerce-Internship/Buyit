namespace Buyit.Application.DTOs;

// Request body for updating the quantity of an existing cart item.
public record UpdateCartItemRequest(int Quantity);
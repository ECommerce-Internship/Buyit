namespace Buyit.Application.DTOs;

// TB-66 (optional): a named-field body for updating stock, e.g. { "quantity": 42 }.
public record UpdateStockRequest(int Quantity);

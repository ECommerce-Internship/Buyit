namespace Buyit.Application.DTOs;

// One product line within a StoreOrder. ProductName/UnitPrice are purchase-time snapshots.
public record StoreOrderItemResponse(
    int StoreOrderItemId,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

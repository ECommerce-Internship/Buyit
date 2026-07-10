namespace Buyit.Application.DTOs;

// The message payload serialized to JSON and sent to the Azure Queue
public record LowStockMessage(
    int ProductId,
    string ProductName,
    int Quantity,
    int Threshold,
    DateTime TriggeredAt,
    int StoreId
);
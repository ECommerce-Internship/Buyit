namespace Buyit.Application.DTOs;

public record InventoryResponse(
	int ProductId,
	string ProductName,
	string Sku,
	int Quantity,
	int LowStockThreshold,
	bool IsLowStock,
	DateTime LastUpdated
);
using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface IInventoryService
{
    Task<IEnumerable<InventoryResponse>> GetAllAsync();
    Task<InventoryResponse> GetByProductIdAsync(int productId);
    Task<InventoryResponse> UpdateStockAsync(int productId, int newQuantity);
    Task<IEnumerable<InventoryResponse>> GetLowStockAsync();
    Task<InventoryResponse> UpdateThresholdAsync(int productId, int newThreshold);
}
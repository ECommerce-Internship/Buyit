using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface IInventoryService
{
    Task<IEnumerable<InventoryResponse>> GetAllAsync();
    Task<InventoryResponse> GetByProductIdAsync(int productId);
    Task<InventoryResponse> UpdateStockAsync(int productId, int newQuantity);
    // sellerUserId: null = platform-wide (admin); a value scopes to that seller's own stores.
    Task<IEnumerable<InventoryResponse>> GetLowStockAsync(int? sellerUserId = null);
    Task<InventoryResponse> UpdateThresholdAsync(int productId, int newThreshold);
    Task<IEnumerable<InventoryResponse>> GetByStoreAsync(int storeId);
}
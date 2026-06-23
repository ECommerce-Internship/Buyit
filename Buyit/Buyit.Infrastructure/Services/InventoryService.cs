using Microsoft.EntityFrameworkCore;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;

namespace Buyit.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _context;
    private readonly ILowStockAlertService _lowStockAlertService;
    private readonly ICacheService _cache;

    public InventoryService(
        AppDbContext context,
        ILowStockAlertService lowStockAlertService,
        ICacheService cache)
    {
        _context = context;
        _lowStockAlertService = lowStockAlertService;
        _cache = cache;
    }

    // GET ALL: Joins Inventory with Products, excludes soft-deleted, ordered by quantity ascending
    public async Task<IEnumerable<InventoryResponse>> GetAllAsync()
    {
        return await _context.Inventories
            .Include(i => i.Product)
            .Where(i => !i.Product.IsDeleted)
            .OrderBy(i => i.QuantityInStock)
            .Select(i => ToResponse(i))
            .ToListAsync();
    }

    // GET BY PRODUCT ID: Throws NotFoundException if product or inventory not found
    public async Task<InventoryResponse> GetByProductIdAsync(int productId)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        return ToResponse(inventory);
    }

    // PUT STOCK: Updates quantity, triggers low stock alert if quantity <= threshold
    public async Task<InventoryResponse> UpdateStockAsync(int productId, int newQuantity)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        inventory.QuantityInStock = newQuantity;
        inventory.LastUpdated = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Stock is part of the cached ProductResponse, so drop this product's caches AFTER
        // the commit (fail-open: a Redis outage won't break the stock update).
        await _cache.InvalidateProductAsync(productId);

        // Trigger low stock alert if quantity dropped to or below threshold
        if (newQuantity <= inventory.LowStockThreshold)
            await _lowStockAlertService.TriggerAlertAsync(productId, inventory.Product.Name, newQuantity, inventory.LowStockThreshold);

        return ToResponse(inventory);
    }

    // GET LOW STOCK: Returns all inventory where quantity <= threshold, ordered by quantity ascending
    public async Task<IEnumerable<InventoryResponse>> GetLowStockAsync()
    {
        return await _context.Inventories
            .Include(i => i.Product)
            .Where(i => !i.Product.IsDeleted && i.QuantityInStock <= i.LowStockThreshold)
            .OrderBy(i => i.QuantityInStock)
            .Select(i => ToResponse(i))
            .ToListAsync();
    }

    // PUT THRESHOLD: Updates the low stock threshold for a product
    public async Task<InventoryResponse> UpdateThresholdAsync(int productId, int newThreshold)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.ProductId == productId);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        inventory.LowStockThreshold = newThreshold;
        inventory.LastUpdated = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ToResponse(inventory);
    }

    // Maps Inventory entity to InventoryResponse DTO
    private static InventoryResponse ToResponse(Domain.Entities.Inventory i) => new(
        i.ProductId,
        i.Product.Name,
        i.Product.Sku,
        i.QuantityInStock,
        i.LowStockThreshold,
        i.QuantityInStock <= i.LowStockThreshold,
        i.LastUpdated
    );
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.Interfaces;
using Buyit.Application.DTOs;

namespace Buyit.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
[Authorize(Roles = "Admin,Seller")]   // per-product writes allow sellers; list views stay Admin-only below
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    // GET api/v1/inventory
    [HttpGet]
    [Authorize(Roles = "Admin")]   // full inventory list is an admin-only view
    public async Task<IActionResult> GetAll()
    {
        var inventory = await _inventoryService.GetAllAsync();
        return Ok(inventory);
    }

    // GET api/v1/inventory/{productId}
    [HttpGet("{productId:int}")]
    public async Task<IActionResult> GetByProductId(int productId)
    {
        var inventory = await _inventoryService.GetByProductIdAsync(productId);
        return Ok(inventory);
    }

    // PUT api/v1/inventory/{productId}/stock
    [HttpPut("{productId:int}/stock")]
    public async Task<IActionResult> UpdateStock(int productId, [FromBody] int newQuantity)
    {
        var inventory = await _inventoryService.UpdateStockAsync(productId, newQuantity);
        return Ok(inventory);
    }

    // PUT api/v1/inventory/{productId}  — friendlier alias matching the TB-66 ticket URL.
    // Body: { "quantity": 42 }. Delegates to the same service method as /{id}/stock.
    [HttpPut("{productId:int}")]
    public async Task<IActionResult> UpdateStockByProduct(int productId, [FromBody] UpdateStockRequest request)
    {
        var inventory = await _inventoryService.UpdateStockAsync(productId, request.Quantity);
        return Ok(inventory);
    }

    // GET api/v1/inventory/low-stock
    [HttpGet("low-stock")]
    [Authorize(Roles = "Admin")]   // platform-wide low-stock list is admin-only
    public async Task<IActionResult> GetLowStock()
    {
        var inventory = await _inventoryService.GetLowStockAsync();
        return Ok(inventory);
    }

    // PUT api/v1/inventory/{productId}/threshold
    [HttpPut("{productId:int}/threshold")]
    public async Task<IActionResult> UpdateThreshold(int productId, [FromBody] int newThreshold)
    {
        var inventory = await _inventoryService.UpdateThresholdAsync(productId, newThreshold);
        return Ok(inventory);
    }
}
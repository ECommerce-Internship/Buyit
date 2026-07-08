using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class ProductTools
{
    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;

    public ProductTools(IProductService productService, IInventoryService inventoryService)
    {
        _productService = productService;
        _inventoryService = inventoryService;
    }

    [McpServerTool, Description("Search for products by name, category, or price range. Matching is case-insensitive and word-order independent. Returns a paginated list; each item has a 'quantityInStock' field — a product is IN STOCK when quantityInStock is greater than 0.")]
    public async Task<string> search_products(
        [Description("Search term to filter by product name")] string? search = null,
        [Description("Category ID to filter by")] int? categoryId = null,
        [Description("Minimum price filter")] decimal? minPrice = null,
        [Description("Maximum price filter")] decimal? maxPrice = null,
        [Description("Page number (default 1)")] int page = 1,
        [Description("Page size (default 10, max 50)")] int pageSize = 10)
    {
        var query = new ProductQueryParameters
        {
            Search = search,
            CategoryId = categoryId,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            Page = page,
            PageSize = pageSize
        };

        var result = await _productService.GetAllAsync(query);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get full details of a single product by its ID.")]
    public async Task<string> get_product(
        [Description("The product ID")] int productId)
    {
        var result = await _productService.GetByIdAsync(productId);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get all products that are at or below their low stock threshold.")]
    public async Task<string> get_low_stock_products()
    {
        var result = await _inventoryService.GetLowStockAsync();
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
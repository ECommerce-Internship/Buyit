namespace Buyit.Application.DTOs;

/// <summary>The data a client submits to create a new product (POST body).</summary>
public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }

    // Which category this product belongs to (an int id in THIS project, not a GUID).
    public int CategoryId { get; set; }

    // The starting stock level — used to create the linked Inventory record.
    public int InitialStock { get; set; }
}
namespace Buyit.Application.DTOs;

/// <summary>
/// The safe, public shape of a product when we send it back to the client.
/// Flat on purpose — no navigation graph, no circular references.
/// </summary>
public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    // Foreign key + a friendly name pulled from the joined Category.
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    // Pulled from the one-to-one Inventory record (QuantityInStock — NOT "Quantity").
    public int QuantityInStock { get; set; }

    // Computed: the mean of all this product's review ratings (0 if no reviews yet).
    public double AverageRating { get; set; }

    // Computed: how many reviews this product has (0 if none yet).
    public int ReviewCount { get; set; }
}

namespace Buyit.Domain.Entities;

/// <summary>A sellable item in the catalogue.</summary>
public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Stock Keeping Unit — the unique business code for the product.
    public string Sku { get; set; } = string.Empty;

    public decimal Price { get; set; }

    // Public URL of the product image (Azure Blob later). Optional.
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft-delete flag. We NEVER hard-delete a product: doing so would be blocked by the
    // OrderItem/CartItem foreign keys and would lose catalogue history. Setting this true
    // hides the product from customers (via a global query filter in AppDbContext) while
    // keeping the row intact for past orders, reviews and reports.
    public bool IsDeleted { get; set; } = false;

    // Each product belongs to exactly one category (1:N — FK on this "many" side).
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // One-to-one: each product has exactly one inventory record.
    public Inventory? Inventory { get; set; }

    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

namespace Buyit.Domain.Entities;

/// <summary>
/// One product line within a StoreOrder. Price and name are SNAPSHOTS taken at
/// purchase time so later product edits never rewrite historical orders.
/// Replaces OrderItem in the marketplace model.
/// </summary>
public class StoreOrderItem
{
    public int Id { get; set; }

    public int StoreOrderId { get; set; }
    public StoreOrder StoreOrder { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    // Price-at-purchase snapshot (do NOT read Product.Price for past orders).
    public decimal UnitPrice { get; set; }

    // Product name as it was at purchase time.
    public string ProductNameSnapshot { get; set; } = string.Empty;

    // UnitPrice * Quantity for this line.
    public decimal Subtotal { get; set; }
}
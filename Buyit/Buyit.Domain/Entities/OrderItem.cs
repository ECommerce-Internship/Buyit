namespace Buyit.Domain.Entities;

/// <summary>
/// A single line in an order. UnitPrice is stored as a SNAPSHOT of the product
/// price at purchase time, so later price changes never rewrite historical orders.
/// </summary>
public class OrderItem
{
    public int Id { get; set; }

    public int Quantity { get; set; }

    // Price-at-purchase snapshot — do NOT read Product.Price for past orders.
    public decimal UnitPrice { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

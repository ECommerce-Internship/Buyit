using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>A placed order with its current status and captured total.</summary>
public class Order
{
    public int Id { get; set; }

    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Total captured at purchase time.
    public decimal TotalAmount { get; set; }

    // Each order belongs to one user.
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    // One-to-one: each order has exactly one payment.
    public Payment? Payment { get; set; }
}

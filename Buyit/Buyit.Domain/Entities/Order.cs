using System.ComponentModel.DataAnnotations;
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

    // ---- Shipping address SNAPSHOT ----
    // Captured onto the order at checkout, NOT read from the User. The customer can change
    // their profile address after ordering, but the order must always ship to where it was
    // placed. Stored as structured fields so we can later filter/report by city or country.
    [Required, MaxLength(200)]
    public string ShippingLine1 { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ShippingLine2 { get; set; }

    [Required, MaxLength(100)]
    public string ShippingCity { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string ShippingPostalCode { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ShippingCountry { get; set; } = string.Empty;

    // Each order belongs to one user.
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    // One-to-one: each order has exactly one payment.
    public Payment? Payment { get; set; }
}

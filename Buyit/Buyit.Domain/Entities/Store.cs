using System.ComponentModel.DataAnnotations;
using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>A seller's shop. One seller (User) can own many stores.</summary>
public class Store
{
    public int Id { get; set; }

    // The seller who owns this store. FK -> User (one user -> many stores).
    public int OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    // URL-friendly unique identifier, e.g. "carls-gadgets". Unique index added in TB-121.
    [Required, MaxLength(160)]
    public string Slug { get; set; } = string.Empty;

    // Optional marketing blurb shown on the storefront. Nullable: a seller may skip it.
    [MaxLength(1000)]
    public string? Description { get; set; }

    // Pending until an admin approves. A store cannot sell unless Approved.
    public StoreStatus Status { get; set; } = StoreStatus.Pending;

    // The platform's cut as a fraction: 0.15 = 15%. Precision (5,4) set in TB-121.
    public decimal CommissionRate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- Navigation properties ----
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<StoreOrder> StoreOrders { get; set; } = new List<StoreOrder>();
}
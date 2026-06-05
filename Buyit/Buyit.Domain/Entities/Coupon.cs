using System.ComponentModel.DataAnnotations;

namespace Buyit.Domain.Entities;

/// <summary>A discount code an admin creates and a customer applies to a cart.</summary>
public class Coupon
{
    public int Id { get; set; }

    // Unique code, e.g. WELCOME10 (uniqueness enforced in AppDbContext).
    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    // Percentage off, e.g. 10 means 10%.
    public decimal DiscountPercentage { get; set; }

    public DateTime ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;

    // One coupon can be applied to many carts.
    public ICollection<Cart> Carts { get; set; } = new List<Cart>();
}

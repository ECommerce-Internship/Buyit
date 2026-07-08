namespace Buyit.Domain.Entities;

/// <summary>A customer's shopping cart.</summary>
public class Cart
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Each cart belongs to one user.
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // A cart may optionally have a coupon applied (nullable FK).
    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}

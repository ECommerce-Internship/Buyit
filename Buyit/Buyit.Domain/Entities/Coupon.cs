using System.ComponentModel.DataAnnotations;
using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>A discount code an admin (global) or seller (own store) creates and a customer applies to a cart.</summary>
public class Coupon
{
    public int Id { get; set; }

    // Unique code, e.g. WELCOME10 (uniqueness enforced in AppDbContext).
    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    // Percentage or fixed-amount off — see DiscountValue for how it's interpreted.
    public CouponDiscountType DiscountType { get; set; } = CouponDiscountType.Percentage;

    // If DiscountType == Percentage: 0-100 (10 means 10%).
    // If DiscountType == FixedAmount: a flat currency amount off the subtotal.
    public decimal DiscountValue { get; set; }

    public DateTime ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Null = unlimited redemptions. Otherwise the coupon stops applying once UsageCount reaches this.
    public int? UsageLimit { get; set; }
    public int UsageCount { get; set; } = 0;

    // Null = platform-wide coupon. Otherwise restricts the coupon to one store.
    public int? StoreId { get; set; }
    public Store? Store { get; set; }

    // One coupon can be applied to many carts.
    public ICollection<Cart> Carts { get; set; } = new List<Cart>();
}

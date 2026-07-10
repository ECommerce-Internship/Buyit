using Buyit.Domain.Enums;

namespace Buyit.Application.DTOs;

/// <summary>The data a client submits to create a new coupon (POST body).</summary>
public class CreateCouponRequest
{
    public string Code { get; set; } = string.Empty;
    public CouponDiscountType DiscountType { get; set; } = CouponDiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int? UsageLimit { get; set; }

    // Null = platform-wide coupon (Admin only). Otherwise the seller's own store.
    public int? StoreId { get; set; }
}

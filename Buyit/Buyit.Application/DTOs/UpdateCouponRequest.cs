using Buyit.Domain.Enums;

namespace Buyit.Application.DTOs;

/// <summary>The data a client submits to update an existing coupon (PUT body). StoreId is not editable.</summary>
public class UpdateCouponRequest
{
    public string Code { get; set; } = string.Empty;
    public CouponDiscountType DiscountType { get; set; } = CouponDiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;
    public int? UsageLimit { get; set; }
}
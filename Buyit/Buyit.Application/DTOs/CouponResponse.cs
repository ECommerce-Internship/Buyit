using Buyit.Domain.Enums;

namespace Buyit.Application.DTOs;

/// <summary>The safe, public shape of a coupon when we send it back to the client.</summary>
public class CouponResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public CouponDiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsActive { get; set; }
    public int? UsageLimit { get; set; }
    public int UsageCount { get; set; }

    // Null = platform-wide coupon.
    public int? StoreId { get; set; }
    public string? StoreName { get; set; }
}
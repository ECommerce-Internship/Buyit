namespace Buyit.Application.DTOs;

/// <summary>Optional filters for GET /coupons.</summary>
public class CouponQueryParameters
{
    // When set: scope to one store (seller must own it; admin may query any store).
    // When omitted: Admin sees everything, Seller sees coupons across all stores they own.
    public int? StoreId { get; set; }
}
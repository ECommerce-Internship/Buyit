namespace Buyit.Application.DTOs;

// Request body for applying a coupon code to the cart.
public record ApplyCouponRequest(string Code);
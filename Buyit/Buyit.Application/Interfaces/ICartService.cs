using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Defines cart operations: retrieving, modifying items, and managing coupons.
public interface ICartService
{
    Task<CartResponse> GetCartAsync(int userId);
    Task<CartResponse> AddItemAsync(int userId, AddCartItemRequest request);
    Task<CartResponse> UpdateItemAsync(int userId, int productId, UpdateCartItemRequest request);
    Task RemoveItemAsync(int userId, int productId);
    Task ClearCartAsync(int userId);
    Task<CartResponse> ApplyCouponAsync(int userId, ApplyCouponRequest request);
    Task<CartResponse> RemoveCouponAsync(int userId);
}
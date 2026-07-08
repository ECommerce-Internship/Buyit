using Buyit.Application.DTOs;
using Buyit.Domain.Enums;

namespace Buyit.Application.Interfaces;

// Order placement and management. The marketplace order fans out into per-store StoreOrders.
public interface IOrderService
{
    // Customer
    Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request);
    Task<PaginatedResult<OrderSummaryResponse>> GetMyOrdersAsync(int userId, int page, int pageSize);
    Task<OrderResponse> GetOrderByIdAsync(int orderId, int userId, bool isAdmin);

    // Cancel one store-slice (buyer for their own pending slice, seller for their store, or admin).
    // Restocks that StoreOrder's inventory.
    Task CancelStoreOrderAsync(int storeOrderId, int callerUserId, bool isAdmin);

    // Admin
    Task<PaginatedResult<OrderSummaryResponse>> GetAllOrdersAsync(int page, int pageSize, string? status, DateTime? from, DateTime? to);

    // Admin: set ONE status for the whole order by applying it to every store-slice at once.
    // Throws ValidationException if any slice can't legally transition (all-or-nothing).
    Task<OrderResponse> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequest request);

    // Seller/Admin: list and update the caller's own StoreOrders.
    Task<PaginatedResult<StoreOrderResponse>> GetMyStoreOrdersAsync(int sellerUserId, int page, int pageSize);
    Task<OrderResponse> UpdateStoreOrderStatusAsync(int storeOrderId, int callerUserId, bool isAdmin, UpdateOrderStatusRequest request);
}

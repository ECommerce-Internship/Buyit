using Buyit.Application.DTOs;
using Buyit.Domain.Enums;

namespace Buyit.Application.Interfaces;

// Defines the order placement and management operations for customers and admins 
public interface IOrderService
{
    // Customer endpoints
    Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request);
    Task<PaginatedResult<OrderSummaryResponse>> GetMyOrdersAsync(int userId, int page, int pageSize);
    Task<OrderResponse> GetOrderByIdAsync(int orderId, int userId, bool isAdmin);
    Task CancelOrderAsync(int orderId, int userId);

    // Admin endpoints
    Task<PaginatedResult<OrderSummaryResponse>> GetAllOrdersAsync(int page, int pageSize, string? status, DateTime? from, DateTime? to);
    Task<OrderResponse> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequest request);
}
using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Defines the order placement operation for the checkout flow.
public interface IOrderService
{
    Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request);
}
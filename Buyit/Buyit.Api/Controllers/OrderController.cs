using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using System.Security.Claims;

namespace Buyit.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize(Roles = "Customer")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    private int GetUserId() => int.Parse(User.FindFirstValue("sub")!);

    // POST api/v1/orders
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var order = await _orderService.PlaceOrderAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetOrderById), new { id = order.OrderId }, order);
    }

    // GET api/v1/orders
    [HttpGet]
    public async Task<IActionResult> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var orders = await _orderService.GetMyOrdersAsync(GetUserId(), page, pageSize);
        return Ok(orders);
    }

    // GET api/v1/orders/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOrderById(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id, GetUserId(), isAdmin: false);
        return Ok(order);
    }

    // PUT api/v1/orders/store-orders/{storeOrderId}/cancel
    // Cancels one store-slice of the buyer's order (restocks that store's inventory).
    [HttpPut("store-orders/{storeOrderId:int}/cancel")]
    public async Task<IActionResult> CancelStoreOrder(int storeOrderId)
    {
        await _orderService.CancelStoreOrderAsync(storeOrderId, GetUserId(), isAdmin: false);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public AdminOrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    // GET api/v1/admin/orders
    [HttpGet]
    public async Task<IActionResult> GetAllOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var orders = await _orderService.GetAllOrdersAsync(page, pageSize, status, from, to);
        return Ok(orders);
    }

    // GET api/v1/admin/orders/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOrderById(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id, userId: 0, isAdmin: true);
        return Ok(order);
    }

    // PUT api/v1/admin/orders/store-orders/{storeOrderId}/status
    [HttpPut("store-orders/{storeOrderId:int}/status")]
    public async Task<IActionResult> UpdateStoreOrderStatus(int storeOrderId, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await _orderService.UpdateStoreOrderStatusAsync(storeOrderId, callerUserId: 0, isAdmin: true, request);
        return Ok(order);
    }
}

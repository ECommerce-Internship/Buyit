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

    // Extracts userId from the JWT "sub" claim
    private int GetUserId() =>
        int.Parse(User.FindFirstValue("sub")!);

    // POST api/v1/orders
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var order = await _orderService.PlaceOrderAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetOrderById), new { id = order.OrderId }, order);
    }

    // GET api/v1/orders
    [HttpGet]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
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

    // PUT api/v1/orders/{id}/cancel
    [HttpPut("{id:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        await _orderService.CancelOrderAsync(id, GetUserId());
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

    // PUT api/v1/admin/orders/{id}/status
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await _orderService.UpdateOrderStatusAsync(id, request);
        return Ok(order);
    }
}
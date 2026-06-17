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
        return CreatedAtAction(nameof(PlaceOrder), new { id = order.OrderId }, order);
    }
}
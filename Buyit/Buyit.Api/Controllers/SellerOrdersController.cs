using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/store-orders")]
[Authorize(Roles = "Seller,Admin")]
public class SellerOrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public SellerOrdersController(IOrderService orders) => _orders = orders;

    /// <summary>List the StoreOrders belonging to the caller's stores.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(await _orders.GetMyStoreOrdersAsync(GetUserId(), page, pageSize));

    /// <summary>Update the status of one of the caller's StoreOrders.</summary>
    [HttpPut("{storeOrderId:int}/status")]
    public async Task<IActionResult> UpdateStatus(int storeOrderId, [FromBody] UpdateOrderStatusRequest request)
        => Ok(await _orders.UpdateStoreOrderStatusAsync(storeOrderId, GetUserId(), isAdmin: false, request));

    private int GetUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(sub, out var userId))
            throw new UnauthorizedException("Token is missing a valid user id.");
        return userId;
    }
}

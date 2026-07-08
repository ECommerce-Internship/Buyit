using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using System.Security.Claims;

namespace Buyit.Api.Controllers;

[ApiController]
[Route("api/v1/cart")]
[Authorize(Roles = "Customer")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    // Extracts the userId from the JWT "sub" claim 
    private int GetUserId() =>
        int.Parse(User.FindFirstValue("sub")!);

    // GET api/v1/cart
    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var cart = await _cartService.GetCartAsync(GetUserId());
        return Ok(cart);
    }

    // POST api/v1/cart/items
    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request)
    {
        var cart = await _cartService.AddItemAsync(GetUserId(), request);
        return Ok(cart);
    }

    // PUT api/v1/cart/items/{productId}
    [HttpPut("items/{productId:int}")]
    public async Task<IActionResult> UpdateItem(int productId, [FromBody] UpdateCartItemRequest request)
    {
        var cart = await _cartService.UpdateItemAsync(GetUserId(), productId, request);
        return Ok(cart);
    }

    // DELETE api/v1/cart/items/{productId}
    [HttpDelete("items/{productId:int}")]
    public async Task<IActionResult> RemoveItem(int productId)
    {
        await _cartService.RemoveItemAsync(GetUserId(), productId);
        return NoContent();
    }

    // DELETE api/v1/cart
    [HttpDelete]
    public async Task<IActionResult> ClearCart()
    {
        await _cartService.ClearCartAsync(GetUserId());
        return NoContent();
    }

    // POST api/v1/cart/coupon
    [HttpPost("coupon")]
    public async Task<IActionResult> ApplyCoupon([FromBody] ApplyCouponRequest request)
    {
        var cart = await _cartService.ApplyCouponAsync(GetUserId(), request);
        return Ok(cart);
    }

    // DELETE api/v1/cart/coupon
    [HttpDelete("coupon")]
    public async Task<IActionResult> RemoveCoupon()
    {
        var cart = await _cartService.RemoveCouponAsync(GetUserId());
        return Ok(cart);
    }
}
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class CartTools
{
    private readonly ICartService _cartService;
    private readonly ICurrentUserService _currentUser;

    public CartTools(ICartService cartService, ICurrentUserService currentUser)
    {
        _cartService = cartService;
        _currentUser = currentUser;
    }

    [McpServerTool, Description("Add a product to the signed-in customer's shopping cart. The cart is always the caller's own; there is no user parameter.")]
    public async Task<string> add_to_cart(
        [Description("The ID of the product to add to the cart")] int productId,
        [Description("How many units to add (must be at least 1)")] int quantity = 1)
    {
        // Identity comes from the JWT (via ICurrentUserService), NEVER from the model.
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to modify your cart.");

        var result = await _cartService.AddItemAsync(userId, new AddCartItemRequest(productId, quantity));
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("View the signed-in customer's current shopping cart, including line items, subtotal, any coupon, and the final total.")]
    public async Task<string> view_cart()
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to view your cart.");

        var result = await _cartService.GetCartAsync(userId);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Change the quantity of a product ALREADY in the signed-in customer's cart. Quantity must be at least 1 — to take an item out entirely, use remove_from_cart. Returns the updated cart.")]
    public async Task<string> update_cart_item(
        [Description("The ID of the product to update")] int productId,
        [Description("The new quantity (at least 1)")] int quantity)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to modify your cart.");

        var result = await _cartService.UpdateItemAsync(userId, productId, new UpdateCartItemRequest(quantity));
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Remove a product entirely from the signed-in customer's cart. Returns the updated cart.")]
    public async Task<string> remove_from_cart(
        [Description("The ID of the product to remove")] int productId)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to modify your cart.");

        await _cartService.RemoveItemAsync(userId, productId);
        var cart = await _cartService.GetCartAsync(userId);   // return the refreshed cart so the model can confirm
        return JsonSerializer.Serialize(cart, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Remove ALL items from the signed-in customer's cart. This empties the cart and cannot be undone — confirm with the user before calling it.")]
    public async Task<string> clear_cart()
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to modify your cart.");

        await _cartService.ClearCartAsync(userId);
        return JsonSerializer.Serialize(new { status = "Cart cleared." }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Apply a coupon / discount code to the signed-in customer's cart. Returns the updated cart with the discount reflected in the total.")]
    public async Task<string> apply_coupon(
        [Description("The coupon code to apply, exactly as the user gave it")] string code)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to apply a coupon.");

        var result = await _cartService.ApplyCouponAsync(userId, new ApplyCouponRequest(code));
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Remove the coupon currently applied to the signed-in customer's cart. Returns the updated cart.")]
    public async Task<string> remove_coupon()
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to change your cart.");

        var result = await _cartService.RemoveCouponAsync(userId);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}

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
}

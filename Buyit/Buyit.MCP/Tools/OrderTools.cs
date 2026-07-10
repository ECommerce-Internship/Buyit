using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class OrderTools
{
    private readonly IOrderService _orderService;
    private readonly ICurrentUserService _currentUser;

    public OrderTools(IOrderService orderService, ICurrentUserService currentUser)
    {
        _orderService = orderService;
        _currentUser = currentUser;
    }

    [McpServerTool, Description("Get full details of a specific order by its ID. When isAdmin is true, userId is not required and can be left as 0.")]
    public async Task<string> get_order(
    [Description("The order ID")] int orderId,
    [Description("Whether the requester is an admin (default true)")] bool isAdmin = true,
    [Description("The user ID — only needed if isAdmin is false")] int userId = 0)

    {
        var result = await _orderService.GetOrderByIdAsync(orderId, userId, isAdmin);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get paginated order history for a specific customer.")]
    public async Task<string> get_customer_orders(
        [Description("The customer's user ID")] int userId,
        [Description("Page number (default 1)")] int page = 1,
        [Description("Page size (default 10)")] int pageSize = 10)
    {
        var result = await _orderService.GetMyOrdersAsync(userId, page, pageSize);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get the signed-in customer's OWN order history (paginated). Always self-scoped to the caller — there is no userId parameter.")]
    public async Task<string> get_my_orders(
        [Description("Page number (default 1)")] int page = 1,
        [Description("Page size (default 10)")] int pageSize = 10)
    {
        // Self-scoped: the id comes from the JWT, so the caller can only ever see their own orders.
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to view your orders.");

        var result = await _orderService.GetMyOrdersAsync(userId, page, pageSize);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get the paginated orders placed against the signed-in SELLER's own store(s). Always self-scoped to the caller's stores — there is no seller/user parameter.")]
    public async Task<string> get_my_store_orders(
        [Description("Page number (default 1)")] int page = 1,
        [Description("Page size (default 10)")] int pageSize = 10)
    {
        // Self-scoped: the seller id comes from the JWT identity, so a seller can only ever
        // see orders for their OWN stores — the model has no parameter to widen this.
        var sellerUserId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to view your store orders.");

        var result = await _orderService.GetMyStoreOrdersAsync(sellerUserId, page, pageSize);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Place an order (checkout) for everything currently in the signed-in customer's cart, shipping to the given address. This places a REAL order and cannot be undone — ALWAYS confirm with the user before calling it, and make sure their cart is not empty. Returns the created order.")]
    public async Task<string> checkout(
        [Description("Shipping address line 1 (street and number)")] string shippingLine1,
        [Description("Shipping city")] string shippingCity,
        [Description("Shipping state or province")] string shippingState,
        [Description("Shipping postal / ZIP code")] string shippingPostalCode,
        [Description("Shipping country")] string shippingCountry,
        [Description("Shipping address line 2 (optional — apartment, suite, etc.)")] string? shippingLine2 = null)
    {
        // Self-scoped: the order is always placed for the JWT's user; the model cannot set who it's for.
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("You must be signed in to place an order.");

        var request = new PlaceOrderRequest(
            shippingLine1, shippingLine2, shippingCity, shippingState, shippingPostalCode, shippingCountry);
        var result = await _orderService.PlaceOrderAsync(userId, request);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
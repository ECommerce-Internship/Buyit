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
}
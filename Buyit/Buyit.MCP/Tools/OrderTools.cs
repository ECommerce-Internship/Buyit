using Buyit.Application.Interfaces;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class OrderTools
{
    private readonly IOrderService _orderService;

    public OrderTools(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [McpServerTool, Description("Get full details of a specific order by its ID. Admin can view any order.")]
    public async Task<string> get_order(
        [Description("The order ID")] int orderId,
        [Description("The user ID making the request")] int userId,
        [Description("Whether the requester is an admin")] bool isAdmin = true)
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
}
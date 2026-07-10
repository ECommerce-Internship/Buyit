using Buyit.Application.Interfaces;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class DashboardTools
{
    private readonly IDashboardService _dashboardService;

    public DashboardTools(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [McpServerTool, Description("Get a full dashboard summary including total orders, revenue, top products, and inventory alerts.")]
    public async Task<string> get_dashboard_summary(
        [Description("Optional seller user ID to filter by store. Leave null for platform-wide summary.")] int? sellerUserId = null)
    {
        var result = await _dashboardService.GetSummaryAsync(sellerUserId);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Get the best-selling products. sellerUserId is set server-side for sellers (scoped to their own stores) and left null for admins (platform-wide) — do not supply it yourself.")]
    public async Task<string> get_top_products(
        [Description("Seller scope — set by the server, not the model. Leave null.")] int? sellerUserId = null)
    {
        var result = await _dashboardService.GetTopProductsAsync(sellerUserId);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
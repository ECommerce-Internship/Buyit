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
}
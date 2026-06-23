using Asp.Versioning;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/dashboard")]
[Authorize(Roles = "Admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IDashboardService _dash;
    public AdminDashboardController(IDashboardService dash) => _dash = dash;

    [HttpGet("summary")] public async Task<IActionResult> Summary() => Ok(await _dash.GetSummaryAsync(null));
    [HttpGet("revenue")] public async Task<IActionResult> Revenue([FromQuery] string period = "month") => Ok(await _dash.GetRevenueByPeriodAsync(period, null));
    [HttpGet("top-products")] public async Task<IActionResult> Top() => Ok(await _dash.GetTopProductsAsync(null));
    [HttpGet("new-customers")] public async Task<IActionResult> NewCustomers([FromQuery] string period = "month") => Ok(await _dash.GetNewCustomersAsync(period, null));
    [HttpGet("orders-by-status")] public async Task<IActionResult> ByStatus() => Ok(await _dash.GetOrdersByStatusAsync(null));
}S
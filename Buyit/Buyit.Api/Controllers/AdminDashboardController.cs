using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    // GET api/v1/admin/dashboard  — everything the dashboard needs in one call (TB-66).
    [HttpGet]
    [ProducesResponseType(typeof(AdminDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> All([FromQuery] string period = "month")
        => Ok(await _dash.GetAdminDashboardAsync(period));

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary() => Ok(await _dash.GetSummaryAsync(null));

    [HttpGet("revenue")]
    [ProducesResponseType(typeof(IReadOnlyList<PeriodPointResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Revenue([FromQuery] string period = "month") => Ok(await _dash.GetRevenueByPeriodAsync(period, null));

    [HttpGet("top-products")]
    [ProducesResponseType(typeof(IReadOnlyList<TopProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Top() => Ok(await _dash.GetTopProductsAsync(null));

    [HttpGet("new-customers")]
    [ProducesResponseType(typeof(IReadOnlyList<PeriodPointResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> NewCustomers([FromQuery] string period = "month") => Ok(await _dash.GetNewCustomersAsync(period, null));

    [HttpGet("orders-by-status")]
    [ProducesResponseType(typeof(IReadOnlyList<StatusCountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ByStatus() => Ok(await _dash.GetOrdersByStatusAsync(null));
}

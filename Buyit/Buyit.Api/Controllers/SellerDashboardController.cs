using Asp.Versioning;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/dashboard")]
[Authorize(Roles = "Seller,Admin")]
public class SellerDashboardController : ControllerBase
{
    private readonly IDashboardService _dash;
    public SellerDashboardController(IDashboardService dash) => _dash = dash;

    [HttpGet("summary")] public async Task<IActionResult> Summary() => Ok(await _dash.GetSummaryAsync(Uid()));
    [HttpGet("revenue")] public async Task<IActionResult> Revenue([FromQuery] string period = "month") => Ok(await _dash.GetRevenueByPeriodAsync(period, Uid()));
    [HttpGet("top-products")] public async Task<IActionResult> Top() => Ok(await _dash.GetTopProductsAsync(Uid()));
    [HttpGet("orders-by-status")] public async Task<IActionResult> ByStatus() => Ok(await _dash.GetOrdersByStatusAsync(Uid()));

    private int Uid()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(sub, out var id)) throw new UnauthorizedException("Token is missing a valid user id.");
        return id;
    }
}
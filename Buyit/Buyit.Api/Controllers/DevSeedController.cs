using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Controllers;

/// <summary>
/// Dev-only utility to populate the dashboards with synthetic buyers, orders, payments
/// and reviews. Gated to Admin AND the Development environment — in any other environment
/// it behaves as if it does not exist (404), so it can never affect real data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dev/seed")]
[Authorize(Roles = "Admin")]
public class DevSeedController : ControllerBase
{
    private readonly IDataSeedService _seed;
    private readonly IHostEnvironment _env;

    public DevSeedController(IDataSeedService seed, IHostEnvironment env)
    {
        _seed = seed;
        _env = env;
    }

    // POST api/v1/dev/seed/demo-data
    [HttpPost("demo-data")]
    [ProducesResponseType(typeof(SeedDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SeedDemoData([FromBody] SeedDataRequest? request)
    {
        if (!_env.IsDevelopment())
            throw new NotFoundException("Demo-data seeding is only available in the Development environment.");

        return Ok(await _seed.SeedDemoDataAsync(request ?? new SeedDataRequest()));
    }
}

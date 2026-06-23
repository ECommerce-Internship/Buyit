using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/stores")]   // => /api/v1/admin/stores
[Authorize(Roles = "Admin")]                          // every action: admin only
public class AdminStoresController : ControllerBase
{
    private readonly IStoreService _stores;
    public AdminStoresController(IStoreService stores) => _stores = stores;

    /// <summary>List stores awaiting approval.</summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(IReadOnlyList<StoreResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StoreResponse>>> Pending()
        => Ok(await _stores.GetPendingStoresAsync());

    [HttpPut("{id:int}/approve")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> Approve(int id) => Ok(await _stores.ApproveAsync(id));

    [HttpPut("{id:int}/reject")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> Reject(int id) => Ok(await _stores.RejectAsync(id));

    [HttpPut("{id:int}/suspend")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> Suspend(int id) => Ok(await _stores.SuspendAsync(id));
}

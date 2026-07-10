using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Roles = "Admin,Seller")]
public class CouponController : ControllerBase
{
    private readonly ICouponService _coupons;

    public CouponController(ICouponService coupons)
    {
        _coupons = coupons;
    }

    /// <summary>List coupons. Admin sees all (or one store via ?storeId=); Seller sees their own stores' coupons.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<CouponResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<CouponResponse>>> GetAll([FromQuery] CouponQueryParameters query)
    {
        var result = await _coupons.GetAllAsync(query);
        return Ok(result);
    }

    /// <summary>Get a single coupon by its id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponResponse>> GetById(int id)
    {
        var result = await _coupons.GetByIdAsync(id);
        return Ok(result);
    }

    /// <summary>Create a coupon. Admin may create global (no StoreId) or for any store; Seller only for their own store.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponResponse>> Create([FromBody] CreateCouponRequest request)
    {
        var result = await _coupons.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id, version = "1.0" }, result);
    }

    /// <summary>Update an existing coupon. Seller (own store) or Admin.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponResponse>> Update(int id, [FromBody] UpdateCouponRequest request)
    {
        var result = await _coupons.UpdateAsync(id, request);
        return Ok(result);
    }

    /// <summary>Deactivate a coupon (soft — flips IsActive off). Seller (own store) or Admin.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id)
    {
        await _coupons.DeactivateAsync(id);
        return NoContent();
    }
}

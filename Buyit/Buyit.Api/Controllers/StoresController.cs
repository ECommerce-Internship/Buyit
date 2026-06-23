using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Buyit.Domain.Exceptions;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]   // => /api/v1/Stores
public class StoresController : ControllerBase
{
    private readonly IStoreService _stores;
    private readonly IProductService _products;

    public StoresController(IStoreService stores, IProductService products)
    {
        _stores = stores;
        _products = products;
    }

    /// <summary>An existing seller (or admin) opens an additional store (starts Pending).</summary>
    [Authorize(Roles = "Seller,Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StoreResponse>> Create([FromBody] CreateStoreRequest request)
    {
        var userId = GetUserId();
        var result = await _stores.CreateStoreForUserAsync(userId, request.StoreName, request.StoreDescription);
        return CreatedAtAction(nameof(GetBySlug), new { slug = result.Slug, version = "1.0" }, result);
    }

    /// <summary>Public: view one approved store by its slug.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> GetBySlug(string slug)
        => Ok(await _stores.GetBySlugAsync(slug));

    /// <summary>Public: products belonging to one approved store.</summary>
    [HttpGet("{slug}/products")]
    [ProducesResponseType(typeof(PaginatedResult<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginatedResult<ProductResponse>>> GetStoreProducts(
        string slug, [FromQuery] ProductQueryParameters query)
        => Ok(await _products.GetByStoreSlugAsync(slug, query));

    // Reads the signed-in user's id from the JWT "sub" claim (MapInboundClaims = false).
    private int GetUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(sub, out var userId))
            throw new UnauthorizedException("Token is missing a valid user id.");
        return userId;
    }
}

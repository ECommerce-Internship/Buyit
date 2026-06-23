using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ai/product-content")]
[Authorize(Roles = "Admin")]
public class ProductContentController : ControllerBase
{
    private readonly IGeminiService _geminiService;

    public ProductContentController(IGeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    // POST api/v1/ai/product-content  → generate marketing content for a product.
    [HttpPost]
    [ProducesResponseType(typeof(ProductContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Generate([FromBody] GenerateProductContentRequest request)
    {
        var content = await _geminiService.GenerateProductContentAsync(
            request.ProductName,
            request.Category,
            request.Specs,
            HttpContext.RequestAborted);

        return Ok(content);
    }
}
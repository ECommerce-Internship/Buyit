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

    /// <summary>
    /// Generate marketing content from free-form inputs — the caller supplies the product name,
    /// category and specs directly. <b>Admin only.</b> Returns a suggestion; nothing is saved.
    /// </summary>
    /// <remarks>
    /// Use this only when the product does not exist yet (e.g. drafting before creation). For an
    /// existing product, prefer <c>POST /api/v1/products/{id}/generate-content</c>, which reads the
    /// name and category from the database so the caller only sends <c>specs</c>.
    /// </remarks>
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
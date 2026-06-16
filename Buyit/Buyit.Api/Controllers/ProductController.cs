using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;  
using Microsoft.AspNetCore.Http;           

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductController : ControllerBase
{
    private readonly IProductService _products;

    public ProductController(IProductService products)
    {
        _products = products;
    }

    /// <summary>Get a paged, filtered, sorted list of products.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResult<ProductResponse>>> GetAll(
        [FromQuery] ProductQueryParameters query)
    {
        var result = await _products.GetAllAsync(query);
        return Ok(result);
    }

    /// <summary>Get a single product by its id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(int id)
    {
        var result = await _products.GetByIdAsync(id);
        return Ok(result);
    }

    /// <summary>Create a new product (and its inventory record).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request)
    {
        var result = await _products.CreateAsync(request);
        // 201 Created + a Location header pointing at GET /products/{id}.
        return CreatedAtAction(nameof(GetById), new { id = result.Id, version = "1.0" }, result);
    }

    /// <summary>Update an existing product.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Update(int id, [FromBody] UpdateProductRequest request)
    {
        var result = await _products.UpdateAsync(id, request);
        return Ok(result);
    }

    /// <summary>Soft-delete a product. Returns 204 No Content.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _products.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>Bulk-import products from an .xlsx file. Admin only.</summary>
    [HttpPost("import")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile file)
    {
        // 1) There must actually be a file with content.
        if (file is null || file.Length == 0)
            return BadRequest("No file was uploaded.");

        // 2) Only modern Excel files (.xlsx) are accepted. Compare lower-cased to ignore case.
        //    "?? string.Empty" guards the rare case where the upload has no file name at all.
        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (extension != ".xlsx")
            return BadRequest("Only .xlsx files are allowed.");

        // 3) Size limit: 10 MB. 10 * 1024 * 1024 bytes = 10 megabytes.
        const long maxBytes = 10L * 1024 * 1024;
        if (file.Length > maxBytes)
            return BadRequest("File must be 10 MB or smaller.");

        // 4) Copy the upload into a rewindable MemoryStream so EPPlus can read it cleanly.
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        memory.Position = 0;   // rewind to the start before reading

        // 5) Hand the stream to the service and return its summary.
        var result = await _products.ImportAsync(memory);
        return Ok(result);
    }
}
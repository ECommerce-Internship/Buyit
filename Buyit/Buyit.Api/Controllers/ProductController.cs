using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;    // TB-156: [EnableRateLimiting]

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductController : ControllerBase
{
    private readonly IProductService _products;
    private readonly ISftpImportService _sftpImportService;
    public ProductController(IProductService products, ISftpImportService sftpImportService)
    {
        _products = products;
        _sftpImportService = sftpImportService;
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

    /// <summary>
    /// Semantic (meaning-based) product search — ranks APPROVED products by cosine similarity to
    /// the query, so "something to keep my coffee hot" can match a thermos. Public.
    /// </summary>
    /// <remarks>
    /// Each call embeds the query via Gemini, so this endpoint is throttled by the dedicated
    /// "semantic-search" policy — far more generous than "chat" (60/min vs 10/min) since a shopper
    /// naturally runs many searches while browsing. Gemini failures surface as 502.
    /// </remarks>
    [HttpGet("search/semantic")]
    [EnableRateLimiting("semantic-search")]   // TB-156: generous per-user/IP throttle for browsing
    [ProducesResponseType(typeof(IReadOnlyList<SemanticSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<SemanticSearchResult>>> SearchSemantic(
        [FromQuery] string q, [FromQuery] int take = 10)
    {
        var results = await _products.SearchSemanticAsync(q, take, HttpContext.RequestAborted);
        return Ok(results);
    }

    /// <summary>
    /// Backfill embeddings for products that don't have one yet (e.g. rows created before TB-156).
    /// Admin only. Bounded to <paramref name="batchSize"/> products per call and re-runnable/idempotent —
    /// re-run until the response's <c>remaining</c> is 0.
    /// </summary>
    [HttpPost("embeddings/backfill")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(BackfillEmbeddingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BackfillEmbeddingsResponse>> BackfillEmbeddings([FromQuery] int batchSize = 100)
    {
        var result = await _products.BackfillEmbeddingsAsync(batchSize, HttpContext.RequestAborted);
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

    /// <summary>Create a new product (and its inventory record). Seller (own store) or Admin.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Seller")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request)
    {
        var result = await _products.CreateAsync(request);
        // 201 Created + a Location header pointing at GET /products/{id}.
        return CreatedAtAction(nameof(GetById), new { id = result.Id, version = "1.0" }, result);
    }

    /// <summary>Update an existing product. Seller (own store) or Admin.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Seller")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Update(int id, [FromBody] UpdateProductRequest request)
    {
        var result = await _products.UpdateAsync(id, request);
        return Ok(result);
    }

    /// <summary>
    /// Generate AI marketing content (description, features, SEO title, meta description) for an
    /// existing product. <b>Admin only.</b>
    /// <para>
    /// This returns a <b>SUGGESTION for review — it is NOT saved.</b> To keep any of it, the admin
    /// must explicitly call <c>PUT /api/v1/products/{id}</c> with the chosen values.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Prefer this endpoint for any product that already exists: it reads the product's name and
    /// category from the database, so the caller only sends <c>specs</c>. Use the free-form
    /// <c>POST /api/v1/ai/product-content</c> only when no product row exists yet (e.g. drafting
    /// before creation), where the caller must supply the name and category explicitly.
    /// </remarks>
    [HttpPost("{id:int}/generate-content")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ProductContentResponse>> GenerateContent(
        int id, [FromBody] GenerateContentRequest request)
    {
        var suggestion = await _products.GenerateContentAsync(id, request);
        return Ok(suggestion);
    }

    /// <summary>Soft-delete a product. Returns 204 No Content. Seller (own store) or Admin.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Seller")]
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
        var error = ValidateXlsxUpload(file);
        if (error is not null) return error;

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        memory.Position = 0;   // rewind to the start before reading

        var result = await _products.ImportAsync(memory);
        return Ok(result);
    }

    /// <summary>Bulk-import products into ONE store from an .xlsx file. Seller (own store) or Admin.</summary>
    [HttpPost("import/{storeId:int}")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ImportResultDto>> ImportForStore(int storeId, IFormFile file)
    {
        var error = ValidateXlsxUpload(file);
        if (error is not null) return error;

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        memory.Position = 0;

        // Store ownership (a seller may only target a store they own; an admin may target any) is
        // enforced inside the service, which throws ForbiddenException (403) on a mismatch.
        var result = await _products.ImportForStoreAsync(memory, storeId);
        return Ok(result);
    }

    // Rejects an upload that isn't a present, <= 10 MB .xlsx file — returning the 400 to send back —
    // or returns null when the file is acceptable. Shared by Import and ImportForStore.
    private ActionResult? ValidateXlsxUpload(IFormFile file)
    {
        // There must actually be a file with content.
        if (file is null || file.Length == 0)
            return BadRequest("No file was uploaded.");

        // Only modern Excel files (.xlsx) are accepted. "?? string.Empty" guards a nameless upload.
        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (extension != ".xlsx")
            return BadRequest("Only .xlsx files are allowed.");

        // Size limit: 10 MB.
        const long maxBytes = 10L * 1024 * 1024;
        if (file.Length > maxBytes)
            return BadRequest("File must be 10 MB or smaller.");

        return null;
    }

    /// <summary>Upload (or replace) a product's image. Admin only. multipart/form-data.</summary>
    [HttpPost("{id:int}/image")]
    [Authorize(Roles = "Admin,Seller")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        // 1) There must be a file with content.
        if (file is null || file.Length == 0)
            return BadRequest("No file was uploaded.");

        // 2) Only jpg/jpeg/png allowed. Check the extension (case-insensitive).
        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png" };
        if (!allowed.Contains(extension))
            return BadRequest("Only .jpg, .jpeg, or .png images are allowed.");

        // 3) Max 5 MB. 5 * 1024 * 1024 bytes = 5 megabytes.
        const long maxBytes = 5L * 1024 * 1024;
        if (file.Length > maxBytes)
            return BadRequest("Image must be 5 MB or smaller.");

        // 4) Delegate: upload + save URL + invalidate cache happen in the service.
        var url = await _products.SetProductImageAsync(id, file);
        return Ok(url);
    }

    /// <summary>Remove a product's image. Admin only.</summary>
    [HttpDelete("{id:int}/image")]
    [Authorize(Roles = "Admin,Seller")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(int id)
    {
        await _products.RemoveProductImageAsync(id);
        return NoContent();
    }

    // Download products Excel from SFTP and import it, for Admin only 
    [HttpPost("import-from-sftp")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ImportResultDto>> ImportFromSftp()
    {
        var result = await _sftpImportService.ImportFromSftpAsync();
        return Ok(result);
    }
}
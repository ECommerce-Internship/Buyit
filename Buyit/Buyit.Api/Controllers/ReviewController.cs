using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using System.Security.Claims;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products/{productId:int}/reviews")]
public class ReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    // Reads the logged-in user's id from the JWT "sub" claim.
    private int GetUserId() => int.Parse(User.FindFirstValue("sub")!);

    // GET api/v1/products/{productId}/reviews  → public, paginated reviews + average + total
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductReviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByProduct(
        int productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _reviewService.GetByProductIdAsync(productId, page, pageSize);
        return Ok(result);
    }

    // POST api/v1/products/{productId}/reviews  → customer submits a review
    [HttpPost]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(int productId, [FromBody] SubmitReviewRequest request)
    {
        var review = await _reviewService.SubmitReviewAsync(GetUserId(), productId, request);
        return CreatedAtAction(nameof(GetByProduct), new { productId }, review);
    }
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews")]
[Authorize(Roles = "Customer")]
public class ReviewItemController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewItemController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    private int GetUserId() => int.Parse(User.FindFirstValue("sub")!);

    // PUT api/v1/reviews/{id}  → customer edits OWN review (within 48h)
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] SubmitReviewRequest request)
    {
        var review = await _reviewService.UpdateReviewAsync(GetUserId(), id, request);
        return Ok(review);
    }

    // DELETE api/v1/reviews/{id}  → customer deletes OWN review
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _reviewService.DeleteReviewAsync(GetUserId(), id);
        return NoContent();
    }
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/reviews")]
[Authorize(Roles = "Admin")]
public class AdminReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public AdminReviewController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    // DELETE api/v1/admin/reviews/{id}  → admin deletes ANY review (no ownership check)
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _reviewService.DeleteAsAdminAsync(id);
        return NoContent();
    }
}
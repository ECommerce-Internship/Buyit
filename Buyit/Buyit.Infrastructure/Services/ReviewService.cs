using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

public class ReviewService : IReviewService
{
    // How long after creation a customer may still edit their review.
    private static readonly TimeSpan EditWindow = TimeSpan.FromHours(48);

    // Upper bound on page size to avoid resource-exhaustion (CWE-770).
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly IValidator<SubmitReviewRequest> _validator;
    private readonly ICacheService _cache;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        AppDbContext context,
        IValidator<SubmitReviewRequest> validator,
        ICacheService cache,
        ILogger<ReviewService> logger)
    {
        _context = context;
        _validator = validator;
        _cache = cache;
        _logger = logger;
    }

    // ---------------------------------------------------------------
    // 1) GET a product's reviews (paginated) + average + total
    // ---------------------------------------------------------------
    public async Task<ProductReviewsResponse> GetByProductIdAsync(int productId, int page, int pageSize)
    {
        // Guard paging inputs (negative OFFSET errors, resource exhaustion).
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // The product must exist (and not be soft-deleted). 404 otherwise.
        var productExists = await _context.Products.AnyAsync(p => p.Id == productId);
        if (!productExists)
            throw new NotFoundException($"Product with ID {productId} was not found.");

        // Base query: all reviews for this product. Build it ONCE, reuse for count/avg/page.
        var query = _context.Reviews.Where(r => r.ProductId == productId);

        var totalCount = await query.CountAsync();

        // Average across ALL ratings (not just this page). 0.0 when there are no reviews.
        double averageRating = totalCount == 0
            ? 0
            : await query.AverageAsync(r => (double)r.Rating);

        // One page, newest first, projected straight into the DTO (only needed columns).
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewResponse(
                r.Id,
                r.ProductId,
                r.UserId,
                r.User.FirstName + " " + r.User.LastName,
                r.Rating,
                r.Comment,
                r.CreatedAt))
            .ToListAsync();

        var pageResult = new PaginatedResult<ReviewResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };

        return new ProductReviewsResponse(averageRating, totalCount, pageResult);
    }

    // ---------------------------------------------------------------
    // 2) POST a review — purchase validation + one-per-product
    // ---------------------------------------------------------------
    public async Task<ReviewResponse> SubmitReviewAsync(int userId, int productId, SubmitReviewRequest request)
    {
        await ValidateAsync(request);

        // Product must exist. 404 otherwise.
        var productExists = await _context.Products.AnyAsync(p => p.Id == productId);
        if (!productExists)
            throw new NotFoundException($"Product with ID {productId} was not found.");

        // PURCHASE VALIDATION (the headline rule):
        // Is there an OrderItem for this product, inside a DELIVERED order owned by this user?
        var hasReceived = await _context.OrderItems.AnyAsync(oi =>
            oi.ProductId == productId &&
            oi.Order.UserId == userId &&
            oi.Order.Status == OrderStatus.Delivered);

        if (!hasReceived)
            throw new ForbiddenException("You can only review products you have received");

        // One review per user per product.
        var alreadyReviewed = await _context.Reviews.AnyAsync(r =>
            r.UserId == userId && r.ProductId == productId);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this product.");

        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Unique index on Review(UserId, ProductId): a concurrent request beat us to it.
            // Surface the business rule (409) instead of a raw 500.
            throw new ConflictException("You have already reviewed this product.");
        }

        // The product's cached AverageRating/ReviewCount just changed — drop its caches.
        await _cache.InvalidateProductAsync(productId);

        _logger.LogInformation(
            "User {UserId} reviewed Product {ProductId} (Review {ReviewId}, Rating {Rating})",
            userId, productId, review.Id, review.Rating);

        return await MapToResponseAsync(review.Id);
    }

    // ---------------------------------------------------------------
    // 3) PUT — edit OWN review within 48 hours
    // ---------------------------------------------------------------
    public async Task<ReviewResponse> UpdateReviewAsync(int userId, int reviewId, SubmitReviewRequest request)
    {
        await ValidateAsync(request);

        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        // Ownership: only the author may edit. 403 otherwise.
        if (review.UserId != userId)
            throw new ForbiddenException("You can only edit your own review.");

        // 48-hour edit window. Too old → 400 ValidationException.
        if (DateTime.UtcNow - review.CreatedAt > EditWindow)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["review"] = ["A review can only be edited within 48 hours of submission."]
            });

        review.Rating = request.Rating;
        review.Comment = request.Comment;

        await _context.SaveChangesAsync();

        // The rating changed, so the product's cached AverageRating is now stale.
        await _cache.InvalidateProductAsync(review.ProductId);

        _logger.LogInformation("User {UserId} updated Review {ReviewId}", userId, reviewId);

        return await MapToResponseAsync(review.Id);
    }

    // ---------------------------------------------------------------
    // 4) DELETE — customer deletes OWN review
    // ---------------------------------------------------------------
    public async Task DeleteReviewAsync(int userId, int reviewId)
    {
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        if (review.UserId != userId)
            throw new ForbiddenException("You can only delete your own review.");

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();

        // Removing a review changes the product's AverageRating/ReviewCount.
        await _cache.InvalidateProductAsync(review.ProductId);

        _logger.LogInformation("User {UserId} deleted Review {ReviewId}", userId, reviewId);
    }

    // ---------------------------------------------------------------
    // 5) DELETE — admin deletes ANY review (no ownership check)
    // ---------------------------------------------------------------
    public async Task DeleteAsAdminAsync(int reviewId)
    {
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();

        // Removing a review changes the product's AverageRating/ReviewCount.
        await _cache.InvalidateProductAsync(review.ProductId);

        _logger.LogInformation("Admin deleted Review {ReviewId}", reviewId);
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    // Runs FluentValidation; throws ValidationException (400) on failure.
    private async Task ValidateAsync(SubmitReviewRequest request)
    {
        var result = await _validator.ValidateAsync(request);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }
    }

    // Re-reads a review and projects it (with reviewer name) into a ReviewResponse.
    private async Task<ReviewResponse> MapToResponseAsync(int reviewId)
    {
        return await _context.Reviews
            .Where(r => r.Id == reviewId)
            .Select(r => new ReviewResponse(
                r.Id,
                r.ProductId,
                r.UserId,
                r.User.FirstName + " " + r.User.LastName,
                r.Rating,
                r.Comment,
                r.CreatedAt))
            .FirstAsync();
    }
}
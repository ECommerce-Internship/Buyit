using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// All review operations for customers and admins.
public interface IReviewService
{
    // Public: a page of a product's reviews + averageRating + totalCount.
    Task<ProductReviewsResponse> GetByProductIdAsync(int productId, int page, int pageSize);

    // Customer: submit a review (enforces purchase validation + one-per-product).
    Task<ReviewResponse> SubmitReviewAsync(int userId, int productId, SubmitReviewRequest request);

    // Customer: edit OWN review, only within 48 hours of creation.
    Task<ReviewResponse> UpdateReviewAsync(int userId, int reviewId, SubmitReviewRequest request);

    // Customer: delete OWN review.
    Task DeleteReviewAsync(int userId, int reviewId);

    // Admin: delete ANY review (no ownership check).
    Task DeleteAsAdminAsync(int reviewId);
}
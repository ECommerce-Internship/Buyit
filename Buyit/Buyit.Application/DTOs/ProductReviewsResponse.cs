namespace Buyit.Application.DTOs;

// The full answer for "GET a product's reviews":
//  - AverageRating: mean of ALL this product's ratings (0 if none yet)
//  - TotalCount:    how many reviews exist in total (across all pages)
//  - Reviews:       ONE page of reviews + paging metadata (reuses PaginatedResult<T>)
public record ProductReviewsResponse(
    double AverageRating,
    int TotalCount,
    PaginatedResult<ReviewResponse> Reviews
);
namespace Buyit.Application.DTOs;

// The body a customer sends when creating OR editing a review.
// Rating is required (validated to 1-5). Comment is optional (max 1000 chars, validated).
public record SubmitReviewRequest
(
    int Rating,
    string? Comment
);
namespace Buyit.Application.DTOs;

// The public shape of ONE review sent back to the client.
// Flat on purpose: a display name string, never the whole User entity.
public record ReviewResponse(
    int ReviewId,
    int ProductId,
    int UserId,
    string ReviewerName,
    int Rating,
    string? Comment,
    DateTime CreatedAt
);
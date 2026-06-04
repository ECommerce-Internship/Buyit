namespace Buyit.Domain.Entities;

/// <summary>
/// A customer's rating plus optional comment for a product.
/// Business rule (enforced in service logic later): only buyers who actually
/// received the product may review it, and only once per product.
/// </summary>
public class Review
{
    public int Id { get; set; }

    // 1 to 5 (range enforced via a check constraint in AppDbContext).
    public int Rating { get; set; }
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

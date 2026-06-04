using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>A person who uses the platform — either a Customer or an Admin.</summary>
public class User
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // Unique — no two users may share an email (enforced in AppDbContext).
    public string Email { get; set; } = string.Empty;

    // We NEVER store the raw password — only a BCrypt hash of it.
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Customer;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- Navigation properties ----
    public Cart? Cart { get; set; }                                              // one-to-one
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

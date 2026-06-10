using System.ComponentModel.DataAnnotations;
using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>A person who uses the platform — either a Customer or an Admin.</summary>
public class User
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    // Unique — no two users may share an email (enforced in AppDbContext).
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    // We NEVER store the raw password — only a BCrypt hash of it.
    [Required, MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Customer;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- Navigation properties ----
    public Cart? Cart { get; set; }                                              // one-to-one
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

using System.ComponentModel.DataAnnotations;

namespace Buyit.Domain.Entities;

/// <summary>
/// Links one of our Users to their account at an external identity provider
/// (e.g. Google). Lets a user sign in without a password in our system.
/// One user may have several (one per provider).
/// </summary>
public class UserExternalLogin
{
    public int Id { get; set; }

    // Which external system authenticated the user, e.g. "Google".
    // Use the AuthProviders constants — never a hand-typed string.
    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    // The provider's permanent, unique ID for this account (Google's "sub" claim).
    [Required, MaxLength(256)]
    public string ProviderUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // FK to the owning user (the "many" side holds the FK).
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
using System.ComponentModel.DataAnnotations;

namespace Buyit.Domain.Entities;

/// <summary>
/// A short-lived, single-use code used to verify account ownership during a password reset.
/// Only the HASH of the code is ever stored — the raw code is emailed once and never persisted.
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Set once this code has been used to reset a password; null means still usable.
    public DateTime? UsedAt { get; set; }

    // FK to the owning user (a user can request multiple resets over time).
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}

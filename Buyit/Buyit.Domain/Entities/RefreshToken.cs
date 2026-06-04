namespace Buyit.Domain.Entities;

/// <summary>
/// A long-lived token used to obtain fresh JWT access tokens.
/// One user can hold many (one per device/session) — supports multi-device login.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Set when rotated/revoked; null means still valid.
    public DateTime? RevokedAt { get; set; }

    // FK to the owning user (the "many" side holds the FK).
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}

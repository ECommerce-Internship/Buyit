namespace Buyit.Application.DTOs;

/// <summary>
/// The handful of facts Google gives us about a user after a successful
/// "Sign in with Google". This is a plain data carrier (a DTO) — no logic.
/// The future OAuth callback fills this in and hands it to ExternalAuthService.
/// </summary>
public class GoogleClaims
{
    /// <summary>
    /// Google's permanent, unique ID for this person (the "sub" claim).
    /// This is the reliable identity key — it never changes, even if the
    /// user changes their Gmail address. Stored as UserExternalLogin.ProviderUserId.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The user's Google email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Whether Google has verified the user actually owns this email address
    /// (the "email_verified" claim). MUST be true before we trust Email as an
    /// identity — otherwise a user could bind a Buyit account to an address
    /// they don't control.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>The user's full display name, e.g. "Carl Ibrahim".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL of the user's Google profile picture (not stored yet).</summary>
    public string? Picture { get; set; }
}
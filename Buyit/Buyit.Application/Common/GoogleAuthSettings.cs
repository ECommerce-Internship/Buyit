namespace Buyit.Application.Common;

/// <summary>
/// Strongly-typed view of the "Authentication:Google" section of configuration.
/// Bound once in Program.cs and injected wherever Google OAuth values are needed.
/// </summary>
public class GoogleAuthSettings
{
    /// <summary>Public identifier for our app, issued by Google. Safe to appear in URLs.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Private app password, issued by Google. NEVER expose to the browser.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Exact callback URL Google sends the browser back to. Must match, character-for-character,
    /// the value registered in Google Cloud Console. Differs per environment, hence config.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;
}
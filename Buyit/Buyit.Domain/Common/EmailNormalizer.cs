namespace Buyit.Domain.Common;

/// <summary>
/// Single source of truth for turning an email into its canonical form.
/// Both the password path (AuthService) and the Google path (ExternalAuthService)
/// MUST normalize through here before storing or looking up an email, so the two
/// flows can never disagree on whether two emails are "the same" (e.g. casing).
/// </summary>
public static class EmailNormalizer
{
    /// <summary>
    /// Trims surrounding whitespace and lower-cases the email using the invariant
    /// culture (ToLowerInvariant avoids culture-specific surprises such as the
    /// Turkish dotless 'i'). Returns the input unchanged when null/blank.
    /// </summary>
    public static string Normalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return email;

        return email.Trim().ToLowerInvariant();
    }
}

namespace Buyit.Domain.Constants;

/// <summary>
/// Canonical names of the external identity providers we support.
/// Always reference these constants instead of hand-typing the string,
/// so a typo becomes a compile error rather than a silent bug.
/// </summary>
public static class AuthProviders
{
    public const string Google = "Google";
}
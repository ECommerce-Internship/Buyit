using System.Text;
using System.Text.RegularExpressions;

namespace Buyit.Domain.Helpers;

/// <summary>Turns a display name into a URL-safe slug (lowercase, hyphenated).</summary>
public static class SlugGenerator
{
    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Lowercase, trim, replace any run of non-alphanumeric chars with a single hyphen,
        // then trim stray leading/trailing hyphens.
        var lower = input.Trim().ToLowerInvariant();
        var slug = Regex.Replace(lower, "[^a-z0-9]+", "-");
        return slug.Trim('-');
    }
}
namespace Buyit.Application.Common;

// Strongly-typed view of the "Gemini" section in appsettings.
// Bound once in Program.cs via Configure<GeminiSettings>(...).
public class GeminiSettings
{
    // The secret key from Google AI Studio. Comes from appsettings.Development.json
    // locally and from environment variables / Azure config in deployed environments.
    public string ApiKey { get; set; } = string.Empty;

    // Which Gemini model to call, e.g. "gemini-2.5-flash".
    public string Model { get; set; } = "gemini-2.5-flash";
}
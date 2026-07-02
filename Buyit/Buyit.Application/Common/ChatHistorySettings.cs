namespace Buyit.Application.Common;

// Strongly-typed view of the "ChatHistory" section in appsettings.
// Bound once in Program.cs via Configure<ChatHistorySettings>(...).
public class ChatHistorySettings
{
    // How long a conversation's history survives in Redis before auto-expiring (AC #3).
    public int TtlHours { get; set; } = 24;

    // History windowing: keep only the most recent N turns so Gemini's context stays bounded.
    public int MaxTurns { get; set; } = 20;
}

using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Buyit.Infrastructure.Services;

// Redis-backed conversation history, keyed PER USER so one user can never read another's.
// Mirrors CacheService: borrows the shared IConnectionMultiplexer and fails open on outages.
public class RedisConversationStore : IConversationStore
{
    private readonly IDatabase _db;
    private readonly ChatHistorySettings _settings;
    private readonly ILogger<RedisConversationStore> _logger;

    public RedisConversationStore(
        IConnectionMultiplexer connection,
        IOptions<ChatHistorySettings> settings,
        ILogger<RedisConversationStore> logger)
    {
        _db = connection.GetDatabase();   // cheap handle to run commands on the shared connection
        _settings = settings.Value;
        _logger = logger;
    }

    // The userId is baked INTO the key. A caller can only ever address their own conversations,
    // so cross-user reads are impossible by construction (AC #4).
    private static string KeyFor(int userId, string conversationId) =>
        $"chat:history:{userId}:{conversationId}";

    public async Task<List<ConversationTurn>> GetAsync(
        string conversationId, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            RedisValue value = await _db.StringGetAsync(KeyFor(userId, conversationId));
            if (value.IsNullOrEmpty)
                return new List<ConversationTurn>();   // no history yet = fresh conversation

            return JsonSerializer.Deserialize<List<ConversationTurn>>(value.ToString())
                   ?? new List<ConversationTurn>();
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            // Fail open: unusable history (Redis down OR corrupt JSON) degrades to no memory,
            // never a 500 — history is best-effort, not a hard dependency.
            _logger.LogWarning(ex, "Unusable history for conversation {ConversationId}; treating as empty.", conversationId);
            return new List<ConversationTurn>();
        }
    }

    public async Task SaveAsync(
        string conversationId, int userId, IReadOnlyList<ConversationTurn> history,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string json = JsonSerializer.Serialize(history);
            await _db.StringSetAsync(
                KeyFor(userId, conversationId),
                json,
                TimeSpan.FromHours(_settings.TtlHours));   // TTL from config (AC #3)
        }
        catch (RedisException ex)
        {
            // Fail open: we couldn't persist, but we won't fail the user's request over it.
            _logger.LogWarning(ex, "Redis unavailable saving conversation {ConversationId}.", conversationId);
        }
    }
}

using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Persists and retrieves a single conversation's turns, scoped to the owning user.
// Backed by Redis (RedisConversationStore), but callers depend only on this contract.
public interface IConversationStore
{
    // Returns the stored turns for this user's conversation, or an EMPTY list if none exist
    // (also empty on a Redis outage — history is best-effort, never a hard dependency).
    Task<List<ConversationTurn>> GetAsync(
        string conversationId, int userId, CancellationToken cancellationToken = default);

    // Persists the conversation's turns with the configured TTL. A Redis outage is a no-op.
    Task SaveAsync(
        string conversationId, int userId, IReadOnlyList<ConversationTurn> history,
        CancellationToken cancellationToken = default);
}

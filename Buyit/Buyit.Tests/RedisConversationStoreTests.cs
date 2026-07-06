using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using System.Text.Json;
using Xunit;

namespace Buyit.Tests;

public class RedisConversationStoreTests
{
    private static (RedisConversationStore store, Mock<IDatabase> db) BuildSut()
    {
        var db = new Mock<IDatabase>();
        var conn = new Mock<IConnectionMultiplexer>();
        conn.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);

        var settings = Options.Create(new ChatHistorySettings { TtlHours = 24, MaxTurns = 20 });
        var logger = new Mock<ILogger<RedisConversationStore>>();
        return (new RedisConversationStore(conn.Object, settings, logger.Object), db);
    }

    [Fact]
    public async Task GetAsync_UsesPerUserKey_AndDeserializes()
    {
        var (store, db) = BuildSut();
        var stored = JsonSerializer.Serialize(new List<ConversationTurn> { new("user", "hi") });
        db.Setup(d => d.StringGetAsync("chat:history:42:convo-1", It.IsAny<CommandFlags>()))
          .ReturnsAsync(stored);

        var result = await store.GetAsync("convo-1", userId: 42);

        result.Should().ContainSingle(t => t.Role == "user" && t.Text == "hi");
    }

    [Fact]
    public async Task GetAsync_DifferentUser_CannotReadAnothersKey()
    {
        var (store, db) = BuildSut();
        // Only user 42's key holds data; user 99 asks with the SAME conversationId.
        db.Setup(d => d.StringGetAsync("chat:history:42:convo-1", It.IsAny<CommandFlags>()))
          .ReturnsAsync(JsonSerializer.Serialize(new List<ConversationTurn> { new("user", "secret") }));
        db.Setup(d => d.StringGetAsync("chat:history:99:convo-1", It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisValue.Null);   // user 99's key doesn't exist

        var result = await store.GetAsync("convo-1", userId: 99);

        result.Should().BeEmpty();   // user 99 gets nothing — AC #4 proven
    }

    [Fact]
    public async Task GetAsync_CorruptJson_FailsOpenToEmpty()
    {
        var (store, db) = BuildSut();
        db.Setup(d => d.StringGetAsync("chat:history:42:convo-1", It.IsAny<CommandFlags>()))
          .ReturnsAsync("this is not valid json");

        var result = await store.GetAsync("convo-1", userId: 42);

        result.Should().BeEmpty();   // corrupt history degrades to no memory, not a 500
    }

    [Fact]
    public async Task SaveAsync_WritesWithConfiguredTtl()
    {
        var (store, db) = BuildSut();

        await store.SaveAsync("convo-1", 42, new List<ConversationTurn> { new("user", "hi") });

        // SE.Redis 2.13 binds StringSetAsync(key, value, TimeSpan) to the Expiration overload:
        // the TimeSpan implicitly becomes an Expiration (EX 86400 = 24h). The ValueCondition 4th
        // arg pins the overload; the Expiration constant asserts the configured TTL.
        db.Verify(d => d.StringSetAsync(
            "chat:history:42:convo-1",
            It.IsAny<RedisValue>(),
            TimeSpan.FromHours(24),
            It.IsAny<ValueCondition>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}

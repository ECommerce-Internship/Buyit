using Buyit.Application.Interfaces;     // the ICacheService contract we implement
using Microsoft.Extensions.Logging;     // ILogger for SEQ log messages
using StackExchange.Redis;              // IConnectionMultiplexer, IDatabase, RedisValue
using System.Text.Json;                 // JsonSerializer (serialize/deserialize)

namespace Buyit.Infrastructure.Services;

/// <summary>
/// Redis-backed implementation of ICacheService.
/// "Wraps" the shared IConnectionMultiplexer: it borrows the connection,
/// gets a lightweight IDatabase from it, and runs the actual commands.
/// </summary>
public class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _connection; // the shared (Singleton) connection
    private readonly IDatabase _db;                       // lightweight command handle
    private readonly ILogger<CacheService> _logger;       // writes HIT/MISS/SET/REMOVE to SEQ

    public CacheService(IConnectionMultiplexer connection, ILogger<CacheService> logger)
    {
        _connection = connection;
        _db = connection.GetDatabase(); // cheap: get a handle to run commands on
        _logger = logger;
    }

    // FAIL-OPEN: the cache is an OPTIMIZATION, never a hard dependency. abortConnect=false
    // only stops Redis from failing app startup — it does NOT protect individual commands,
    // which still throw RedisException (e.g. RedisConnectionException/RedisTimeoutException)
    // when Redis is unreachable at runtime. So every method below swallows Redis faults:
    // reads degrade to a MISS (caller falls back to the database) and writes/removes become
    // no-ops. We log a Warning so the outage is visible without taking the endpoint down.

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            // Ask Redis for the text stored at this key.
            RedisValue value = await _db.StringGetAsync(key);

            // IsNullOrEmpty is true when the key doesn't exist (or holds nothing) = a MISS.
            if (value.IsNullOrEmpty)
            {
                _logger.LogInformation("Cache MISS for key {CacheKey}", key);
                return default; // 'default' for a class is null -> signals "not found".
            }

            // HIT: turn the stored JSON text back into a real C# object of type T.
            _logger.LogInformation("Cache HIT for key {CacheKey}", key);
            return JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (RedisException ex)
        {
            // Redis is down/slow: treat as a MISS so the caller queries the database instead.
            _logger.LogWarning(ex, "Redis unavailable on GET for key {CacheKey}; falling back to source.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        try
        {
            // Turn the C# object into a JSON text string...
            string json = JsonSerializer.Serialize(value);

            // ...and store it with a time-to-live, after which Redis auto-deletes it.
            await _db.StringSetAsync(key, json, expiry);

            _logger.LogInformation(
                "Cache SET for key {CacheKey} (expires in {Minutes} min)", key, expiry.TotalMinutes);
        }
        catch (RedisException ex)
        {
            // Couldn't cache the value — no problem, the next read just re-queries the source.
            _logger.LogWarning(ex, "Redis unavailable on SET for key {CacheKey}; skipping cache write.", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
            _logger.LogInformation("Cache REMOVE for key {CacheKey}", key);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable on REMOVE for key {CacheKey}; skipping.", key);
        }
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        try
        {
            // A pattern delete must scan the server's keyspace. Redis can be made of several
            // "endpoints" (servers); we walk each one and ask for keys matching the pattern.
            foreach (var endpoint in _connection.GetEndPoints())
            {
                IServer server = _connection.GetServer(endpoint);

                // server.Keys(pattern) uses the SCAN command under the hood (safe for production).
                foreach (RedisKey key in server.Keys(pattern: pattern))
                {
                    await _db.KeyDeleteAsync(key);
                }
            }

            _logger.LogInformation("Cache REMOVE-BY-PATTERN for pattern {Pattern}", pattern);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable on REMOVE-BY-PATTERN for pattern {Pattern}; skipping.", pattern);
        }
    }

    public async Task InvalidateProductAsync(int productId)
    {
        // Drop the one product entry AND all list pages (any of which may embed this product's
        // stock/rating). Both calls are already fail-open, so a Redis outage just no-ops here.
        await RemoveAsync($"product:{productId}");
        await RemoveByPatternAsync("products:*");
    }
}
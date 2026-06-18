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

    public async Task<T?> GetAsync<T>(string key)
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

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        // Turn the C# object into a JSON text string...
        string json = JsonSerializer.Serialize(value);

        // ...and store it with a time-to-live, after which Redis auto-deletes it.
        await _db.StringSetAsync(key, json, expiry);

        _logger.LogInformation(
            "Cache SET for key {CacheKey} (expires in {Minutes} min)", key, expiry.TotalMinutes);
    }

    public async Task RemoveAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
        _logger.LogInformation("Cache REMOVE for key {CacheKey}", key);
    }

    public async Task RemoveByPatternAsync(string pattern)
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
}
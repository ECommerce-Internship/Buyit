namespace Buyit.Application.Interfaces;

/// <summary>
/// The contract for a key/value cache (backed by Redis).
/// Services depend on THIS, not on the concrete CacheService, so the
/// caching technology can be swapped without touching business code.
/// </summary>
public interface ICacheService
{
    // READ: return the cached object of type T for this key, or null/default if not found.
    Task<T?> GetAsync<T>(string key);

    // WRITE: store value under key as JSON, auto-deleted after expiry.
    Task SetAsync<T>(string key, T value, TimeSpan expiry);

    // DELETE: remove a single key.
    Task RemoveAsync(string key);

    // DELETE MANY: remove all keys matching a pattern.
    Task RemoveByPatternAsync(string pattern);

    // INVALIDATE ONE PRODUCT: drop the single product:{id} entry AND every products:* list
    // page. Centralized here so every write path that changes a product's cached shape
    // (stock, rating, fields) uses the EXACT same key scheme and can't drift.
    Task InvalidateProductAsync(int productId);
}

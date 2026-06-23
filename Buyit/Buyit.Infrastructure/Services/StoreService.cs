using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Domain.Helpers;
using Buyit.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buyit.Infrastructure.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _db;
    private readonly ILogger<StoreService> _logger;

    public StoreService(AppDbContext db, ILogger<StoreService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<StoreResponse> CreateStoreForUserAsync(int ownerUserId, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["storeName"] = new[] { "Store name is required." }
            });

        var slug = await GenerateUniqueSlugAsync(name);

        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            Description = description,
            Slug = slug,
            Status = StoreStatus.Pending,   // every new store starts Pending
            CommissionRate = 0.15m,         // default platform cut; admins can tune later
            CreatedAt = DateTime.UtcNow
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created store {StoreId} ({Slug}) for user {UserId}", store.Id, store.Slug, ownerUserId);
        return Map(store);
    }

    public async Task<IReadOnlyList<StoreResponse>> GetPendingStoresAsync()
    {
        // Materialize first, THEN map: EF Core can't translate the Map(...) call to SQL.
        var stores = await _db.Stores
            .Where(s => s.Status == StoreStatus.Pending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
        return stores.Select(Map).ToList();
    }

    public Task<StoreResponse> ApproveAsync(int id) => SetStatusAsync(id, StoreStatus.Approved);
    public Task<StoreResponse> SuspendAsync(int id) => SetStatusAsync(id, StoreStatus.Suspended);
    public Task<StoreResponse> RejectAsync(int id) => SetStatusAsync(id, StoreStatus.Suspended); // reject == keep out of sale

    public async Task<StoreResponse> GetBySlugAsync(string slug)
    {
        var store = await _db.Stores
            .FirstOrDefaultAsync(s => s.Slug == slug && s.Status == StoreStatus.Approved);
        if (store is null)
            throw new NotFoundException($"No approved store with slug '{slug}' was found.");
        return Map(store);
    }

    // ---- helpers ----
    private async Task<StoreResponse> SetStatusAsync(int id, StoreStatus status)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == id);
        if (store is null)
            throw new NotFoundException($"Store with id {id} was not found.");

        store.Status = status;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Store {StoreId} status set to {Status}", id, status);
        return Map(store);
    }

    private async Task<string> GenerateUniqueSlugAsync(string name)
    {
        var baseSlug = SlugGenerator.Generate(name);
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "store";

        var slug = baseSlug;
        var suffix = 2;
        // Keep trying until the slug is free (respects the unique index).
        while (await _db.Stores.AnyAsync(s => s.Slug == slug))
            slug = $"{baseSlug}-{suffix++}";
        return slug;
    }

    private static StoreResponse Map(Store s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Slug = s.Slug,
        Description = s.Description,
        Status = s.Status.ToString(),
        CreatedAt = s.CreatedAt
    };
}
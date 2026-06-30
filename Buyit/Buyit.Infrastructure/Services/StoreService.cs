using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Constants;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Domain.Helpers;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _db;
    private readonly IValidator<CreateStoreRequest> _createStoreValidator;
    private readonly ILogger<StoreService> _logger;

    public StoreService(
        AppDbContext db,
        IValidator<CreateStoreRequest> createStoreValidator,
        ILogger<StoreService> logger)
    {
        _db = db;
        _createStoreValidator = createStoreValidator;
        _logger = logger;
    }

    public async Task<StoreResponse> CreateStoreForUserAsync(int ownerUserId, string name, string? description)
    {
        // M4: validate via FluentValidation (length + required) -> 400, not a DB 500.
        var validation = await _createStoreValidator.ValidateAsync(
            new CreateStoreRequest { StoreName = name, StoreDescription = description });
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        var slug = await GenerateUniqueSlugAsync(name);

        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            Description = description,
            Slug = slug,
            Status = StoreStatus.Pending,            // every new store starts Pending
            CommissionRate = MarketplaceDefaults.CommissionRate,
            CreatedAt = DateTime.UtcNow
        };
        _db.Stores.Add(store);

        // L3: the slug check-then-insert can race; the unique index is the backstop. Map the
        // Postgres unique-violation (23505) to a clean 409 instead of leaking a 500.
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException("A store with a conflicting slug already exists. Please try again.");
        }

        _logger.LogInformation("Created store {StoreId} ({Slug}) for user {UserId}", store.Id, store.Slug, ownerUserId);
        return Map(store);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    public async Task<IReadOnlyList<StoreResponse>> GetStoresForUserAsync(int ownerUserId)
    {
        // Every store this user owns, regardless of status (unlike GetBySlug which is
        // Approved-only). Materialize first, THEN map — EF can't translate Map(...) to SQL.
        var stores = await _db.Stores
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return stores.Select(Map).ToList();
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
    public Task<StoreResponse> RejectAsync(int id) => SetStatusAsync(id, StoreStatus.Rejected);

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
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Http;   // IFormFile (TB-42)
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;   // TB-156: CosineDistance() -> Postgres "<=>" translation
using OfficeOpenXml;
using System.Globalization;
using System.Security.Cryptography;   
using System.Text;                  
using ValidationException = Buyit.Domain.Exceptions.ValidationException;
using System.Text.Json;

namespace Buyit.Infrastructure.Services;

/// <summary>The real implementation of IProductService — talks to the database via EF Core.</summary>
public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly IValidator<CreateProductRequest> _createValidator;
    private readonly IValidator<UpdateProductRequest> _updateValidator;
    private readonly ICacheService _cache;
    private readonly IBlobStorageService _blob;   // TB-42: uploads/deletes product images
    private readonly ICurrentUserService _currentUser;   // TB-125: ownership checks
    private readonly ILogger<ProductService> _logger;
    private readonly IGeminiService _gemini;                              // TB-47: AI content generator
    private readonly IValidator<GenerateContentRequest> _generateContentValidator;  // TB-47
    private readonly IEmbeddingService _embeddings;                       // TB-156: semantic-search embeddings

    public ProductService(
     AppDbContext db,
     IValidator<CreateProductRequest> createValidator,
     IValidator<UpdateProductRequest> updateValidator,
     ICacheService cache,
     IBlobStorageService blob,            // <-- new (TB-42)
     ICurrentUserService currentUser,     // <-- new (TB-125)
     IGeminiService gemini,                                          // <-- new (TB-47)
     IValidator<GenerateContentRequest> generateContentValidator,    // <-- new (TB-47)
     IEmbeddingService embeddings,                                   // <-- new (TB-156)
     ILogger<ProductService> logger)
    {
        _db = db;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _cache = cache;
        _blob = blob;                        // <-- new (TB-42)
        _currentUser = currentUser;          // <-- new (TB-125)
        _logger = logger;
        _gemini = gemini;                                           // <-- new (TB-47)
        _generateContentValidator = generateContentValidator;      // <-- new (TB-47)
        _embeddings = embeddings;                                   // <-- new (TB-156)
    }

    // TB-125: throws 403 unless the caller is an admin or owns the given store.
    private async Task EnsureCanManageStoreAsync(int storeId)
    {
        if (_currentUser.IsAdmin) return;                       // admin bypasses

        var userId = _currentUser.UserId;
        if (userId is null)
            throw new ForbiddenException("You are not allowed to manage this product.");

        var ownsStore = await _db.Stores.AnyAsync(s => s.Id == storeId && s.OwnerUserId == userId);
        if (!ownsStore)
            throw new ForbiddenException("You can only manage products in your own store.");
    }

    // Turns the raw FeaturesJson column into the public Features list. Malformed JSON
    // (shouldn't happen — we're the only writer) is treated as "no features" rather
    // than throwing, so one bad row can't break an entire list request.
    private static void PopulateFeatures(ProductResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.FeaturesJson)) return;
        try
        {
            var features = JsonSerializer.Deserialize<List<string>>(response.FeaturesJson)
                ?.Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();
            response.Features = features is { Count: > 0 } ? features : null;
        }
        catch (JsonException)
        {
            response.Features = null;
        }
    }

    private static void PopulateFeatures(IEnumerable<ProductResponse> responses)
    {
        foreach (var r in responses) PopulateFeatures(r);
    }

    public async Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters query)
    {
        // Authorization MUST run on every call, before any cache lookup. The cache key is built
        // from the query shape, not the caller's identity — so checking this only on a cache
        // miss would let one seller's legitimately-cached page get served to a DIFFERENT caller
        // who doesn't own that store at all, with no ownership check ever executing.
        if (query.StoreId is not null)
            await EnsureCanManageStoreAsync(query.StoreId.Value);

        // --- CACHE-ASIDE (read): try the cache before touching the database. ---
        string cacheKey = BuildListCacheKey(query);
        var cached = await _cache.GetAsync<PaginatedResult<ProductResponse>>(cacheKey);
        if (cached is not null)
            return cached;   // HIT: return the saved page; DB is never queried.
        _logger.LogInformation("Querying DATABASE for products list (key {CacheKey})", cacheKey);
        // STAGE 1 — start the query. Nothing runs yet; this is an IQueryable (a plan).
        // The global query filter in AppDbContext already excludes IsDeleted == true.
        IQueryable<Product> products = _db.Products;
        if (query.StoreId is not null)
        {
            // Management view (a seller's own store, or an admin managing any store): bypass
            // the public "Approved only" restriction below — the caller needs to see products
            // from a Pending/Suspended store too. Ownership was already checked above.
            products = products.Where(p => p.StoreId == query.StoreId.Value);
        }
        else
        {
            // Marketplace (TB-124): public browsing shows only products from APPROVED stores.
            products = products.Where(p => p.Store.Status == StoreStatus.Approved);
        }

        // STAGE 2 — FILTERING. Each filter is applied ONLY if the client supplied it.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Tokenised, case-INSENSITIVE match. We split the search into words and require EACH
            // word to appear in the name (in any order). EF.Functions.ILike => Postgres "ILIKE",
            // the case-insensitive form of LIKE. This means "laptop", "LAPTOP" and "15-inch laptop"
            // all match a product named "Laptop 15-inch" — plain .Contains() (LIKE) is case-SENSITIVE
            // and whole-phrase only, so those queries returned nothing (TB bot-fix).
            foreach (var term in query.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var pattern = $"%{term}%";
                products = products.Where(p => EF.Functions.ILike(p.Name, pattern));
            }
        }

        if (query.CategoryId is not null)
        {
            products = products.Where(p => p.CategoryId == query.CategoryId);
        }

        if (query.MinPrice is not null)
        {
            products = products.Where(p => p.Price >= query.MinPrice);
        }

        if (query.MaxPrice is not null)
        {
            products = products.Where(p => p.Price <= query.MaxPrice);
        }

        // STAGE 3 — COUNT the whole filtered set (BEFORE paging) for the metadata.
        var totalCount = await products.CountAsync();

        // STAGE 4 — SORTING. Pick the column, then the direction.
        // Id is added as a final tie-breaker so paging is DETERMINISTIC: when two rows
        // share the same Name/Price/CreatedAt, SQL Server's OFFSET/FETCH could otherwise
        // return the same row on two pages (or skip one). A unique column breaks all ties.
        products = (query.SortBy?.ToLower()) switch
        {
            "price" => query.SortDescending ? products.OrderByDescending(p => p.Price).ThenBy(p => p.Id)
                                            : products.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "createdat" => query.SortDescending ? products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
                                                : products.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            // Default and "name" both sort by Name, with Id as the stable tie-breaker.
            _ => query.SortDescending ? products.OrderByDescending(p => p.Name).ThenBy(p => p.Id)
                                      : products.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        // STAGE 5 — PAGING. Skip earlier pages, take this page's slice.
        var items = await products
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            // STAGE 6 — PROJECTION into the DTO (EF fetches only what we use).
            .Select(p => new ProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Sku = p.Sku,
                Price = p.Price,
                ImageUrl = p.ImageUrl,
                CreatedAt = p.CreatedAt,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,                       // joined from Category
                StoreId = p.StoreId,
                StoreName = p.Store.Name,
                StoreSlug = p.Store.Slug,
                QuantityInStock = p.Inventory != null ? p.Inventory.QuantityInStock : 0,
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                ReviewCount = p.Reviews.Count,
                SeoTitle = p.SeoTitle,
                MetaDescription = p.MetaDescription,
                FeaturesJson = p.FeaturesJson

            })
            .ToListAsync();   // <-- THE database is hit HERE, exactly once.
            PopulateFeatures(items);
        // Compute total pages = ceil(totalCount / pageSize). We cast to double on purpose so
        // the division keeps its remainder (e.g. 25/10 = 2.5 -> Ceiling -> 3), then back to int.
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        // Assemble the page + metadata.
        var result = new PaginatedResult<ProductResponse>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };

        // --- CACHE-ASIDE (write): save the freshly-built page for 5 minutes. ---
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));

        return result;
    }

    public async Task<ProductResponse> GetByIdAsync(int id)
    {
        // --- CACHE-ASIDE (read): individual product cached under "product:{id}". ---
        string cacheKey = $"product:{id}";
        var product = await _cache.GetAsync<ProductResponse>(cacheKey);
        if (product is null)
        {
            _logger.LogInformation("Querying DATABASE for product {ProductId}", id);

            // Project straight into the DTO; the global filter still hides soft-deleted rows.
            product = await _db.Products
                .Where(p => p.Id == id)
                .Select(p => new ProductResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Sku = p.Sku,
                    Price = p.Price,
                    ImageUrl = p.ImageUrl,
                    CreatedAt = p.CreatedAt,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category.Name,
                    StoreId = p.StoreId,
                    StoreName = p.Store.Name,
                    StoreSlug = p.Store.Slug,
                    QuantityInStock = p.Inventory != null ? p.Inventory.QuantityInStock : 0,
                    AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                    ReviewCount = p.Reviews.Count,
                    SeoTitle = p.SeoTitle,
                    MetaDescription = p.MetaDescription,
                    FeaturesJson = p.FeaturesJson
                })
                .FirstOrDefaultAsync();

            // No row matched (wrong id, or the product is soft-deleted) -> 404 via middleware.
            if (product is null)
                throw new NotFoundException($"Product with id {id} was not found.");
                PopulateFeatures(product);
            // --- CACHE-ASIDE (write): save this product for 5 minutes. ---
            await _cache.SetAsync(cacheKey, product, TimeSpan.FromMinutes(5));
        }

        // M3: a product whose store isn't Approved is hidden from the public (browsing rule),
        // but the owning seller and admins may still fetch it (e.g. the create/update re-fetch).
        await EnsureProductVisibleAsync(product.StoreId, id);

        return product;
    }

    // Throws 404 if the product's store isn't Approved AND the caller is neither an admin nor
    // the store owner. Keeps non-approved stores out of public browsing while letting owners
    // and admins (and the internal create/update re-fetch they trigger) see their own products.
    private async Task EnsureProductVisibleAsync(int storeId, int productId)
    {
        var store = await _db.Stores
            .Where(s => s.Id == storeId)
            .Select(s => new { s.Status, s.OwnerUserId })
            .FirstOrDefaultAsync();

        if (store is null)
            throw new NotFoundException($"Product with id {productId} was not found.");

        if (store.Status == StoreStatus.Approved) return;
        if (_currentUser.IsAdmin) return;
        if (_currentUser.UserId == store.OwnerUserId) return;

        throw new NotFoundException($"Product with id {productId} was not found.");
    }
    public async Task<PaginatedResult<ProductResponse>> GetByStoreSlugAsync(string slug, ProductQueryParameters query)
    {
        // The store must exist AND be approved to be publicly browsable.
        var store = await _db.Stores
            .FirstOrDefaultAsync(s => s.Slug == slug && s.Status == StoreStatus.Approved);
        if (store is null)
            throw new NotFoundException($"No approved store with slug '{slug}' was found.");

        IQueryable<Product> products = _db.Products.Where(p => p.StoreId == store.Id);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Case-insensitive, tokenised match (same as GetAllAsync — see the comment there).
            foreach (var term in query.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var pattern = $"%{term}%";
                products = products.Where(p => EF.Functions.ILike(p.Name, pattern));
            }
        }
        if (query.CategoryId is not null)
            products = products.Where(p => p.CategoryId == query.CategoryId);
        if (query.MinPrice is not null)
            products = products.Where(p => p.Price >= query.MinPrice);
        if (query.MaxPrice is not null)
            products = products.Where(p => p.Price <= query.MaxPrice);

        var totalCount = await products.CountAsync();

        products = products.OrderBy(p => p.Name).ThenBy(p => p.Id);

        var items = await products
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Sku = p.Sku,
                Price = p.Price,
                ImageUrl = p.ImageUrl,
                CreatedAt = p.CreatedAt,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,
                StoreId = p.StoreId,
                StoreName = p.Store.Name,
                StoreSlug = p.Store.Slug,
                QuantityInStock = p.Inventory != null ? p.Inventory.QuantityInStock : 0,
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                SeoTitle = p.SeoTitle,
                MetaDescription = p.MetaDescription,
                FeaturesJson = p.FeaturesJson,
                ReviewCount = p.Reviews.Count
            })
            .ToListAsync();
            PopulateFeatures(items);

        return new PaginatedResult<ProductResponse>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request)
    {
        // 1) VALIDATE shape -> 400 via middleware if it fails (same pattern as AuthService).
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // 2a) Ownership (TB-125): a seller may only create in their own store; admin -> any store.
        await EnsureCanManageStoreAsync(request.StoreId);

        // 2b) The category must actually exist (validator only checked it's > 0).
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
            throw new NotFoundException($"Category with id {request.CategoryId} was not found.");

        // 2c) SKU must be unique WITHIN THE STORE -> 409 if already used (matches the composite
        //     (StoreId, Sku) index). IgnoreQueryFilters() so a soft-deleted SKU still counts.
        var skuTaken = await _db.Products
            .IgnoreQueryFilters()
            .AnyAsync(p => p.StoreId == request.StoreId && p.Sku == request.Sku);
        if (skuTaken)
            throw new ConflictException($"A product with SKU '{request.Sku}' already exists in this store.");

        // 3) Build the Product TOGETHER WITH its one-to-one Inventory record.
        //    Setting the Inventory navigation property lets EF insert BOTH rows inside a
        //    SINGLE SaveChanges (one transaction): either both succeed or neither does.
        //    This avoids the previous two-save approach, where the product could be created
        //    but the inventory insert could fail, leaving a product with no stock record.
        var cleanedFeatures = request.Features?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Sku = request.Sku,
            Price = request.Price,
            ImageUrl = request.ImageUrl,
            CategoryId = request.CategoryId,
            StoreId = request.StoreId,
            SeoTitle = request.SeoTitle,
            MetaDescription = request.MetaDescription,
            FeaturesJson = cleanedFeatures is { Count: > 0 } ? JsonSerializer.Serialize(cleanedFeatures) : null,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
            Inventory = new Inventory
            {
                QuantityInStock = request.InitialStock
                // ProductId is set automatically by EF from the navigation link above.
            }
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();   // inserts Product + Inventory atomically; sets product.Id.

        // TB-156: generate the semantic-search embedding now that the row exists. Best-effort —
        // a Gemini outage must NOT fail the create; the backfill/lazy path fills the gap later.
        var categoryName = await _db.Categories
            .Where(c => c.Id == request.CategoryId)
            .Select(c => c.Name)
            .FirstAsync();
        await TryUpdateEmbeddingAsync(product, categoryName);

        // --- INVALIDATE: a new product can appear in list results, so drop its caches.
        await _cache.InvalidateProductAsync(product.Id);

        // 4) Return the freshly created product in DTO form (re-fetch to include CategoryName).
        return await GetByIdAsync(product.Id);
    }
    public async Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request)
    {
        // 1) VALIDATE the incoming shape -> 400 if bad.
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // 2) LOAD the tracked entity (NOT a projection) so EF can save changes to it.
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

        // Ownership (TB-125): only the owning seller (or an admin) may update this product.
        await EnsureCanManageStoreAsync(product.StoreId);

        // 3) The new category must exist too.
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
            throw new NotFoundException($"Category with id {request.CategoryId} was not found.");

        // TB-156: capture the embedding-relevant fields BEFORE we overwrite them, so we only
        // re-embed (a paid Gemini call) when the name, description, or category actually changed.
        bool textChanged = product.Name != request.Name
                        || product.Description != request.Description
                        || product.CategoryId != request.CategoryId;

        // 4) COPY the editable fields. EF marks each changed column 'dirty'.
        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.ImageUrl = request.ImageUrl;
        product.CategoryId = request.CategoryId;
        product.SeoTitle = request.SeoTitle;
        product.MetaDescription = request.MetaDescription;
        var cleanedFeatures = request.Features?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        product.FeaturesJson = cleanedFeatures is { Count: > 0 } ? JsonSerializer.Serialize(cleanedFeatures) : null;

        // 5) One UPDATE statement is sent here.
        await _db.SaveChangesAsync();

        // TB-156: re-embed only when the searchable text changed (best-effort, same as create).
        if (textChanged)
        {
            var categoryName = await _db.Categories
                .Where(c => c.Id == product.CategoryId)
                .Select(c => c.Name)
                .FirstAsync();
            await TryUpdateEmbeddingAsync(product, categoryName);
        }

        // --- INVALIDATE: the product changed, so drop its single cache AND all list caches.
        await _cache.InvalidateProductAsync(id);

        // 6) Return the updated product in DTO form.
        return await GetByIdAsync(product.Id);
    }

    // TB-47: generate AI marketing content for an existing product.
    // Returns a SUGGESTION for the admin to review; nothing is persisted.
    public async Task<ProductContentResponse> GenerateContentAsync(int id, GenerateContentRequest request)
    {
        // 1) VALIDATE the request shape -> 400 if bad (specs required, max 500 chars).
        var validation = await _generateContentValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        // 2) LOAD the product AND its Category. The Include is REQUIRED so that
        //    product.Category.Name is populated (without it, Category would be null).
        var product = await _db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

        // 3) Ask the AI service to draft content, feeding values FROM the product row.
        var content = await _gemini.GenerateProductContentAsync(
            product.Name,
            product.Category.Name,
            request.Specs);

        // 4) Return the suggestion. We DELIBERATELY do not save it — the admin must
        //    call PUT /products/{id} to keep anything (TB-47 requirement).
        return content;
    }

    // ===================== TB-156: SEMANTIC SEARCH =====================

    // (Re)generate a product's semantic embedding from name + description + category.
    // Best-effort: if the AI call fails we log and leave the vector unchanged/null so the WRITE
    // still succeeds. Backfill and the query-time path cover the gap later.
    private async Task TryUpdateEmbeddingAsync(Product product, string categoryName)
    {
        var text = $"{product.Name}\n{product.Description}\n{categoryName}";
        try
        {
            var vector = await _embeddings.EmbedAsync(text);
            product.Embedding = new Pgvector.Vector(vector);
            await _db.SaveChangesAsync();
        }
        catch (ExternalServiceException ex)
        {
            _logger.LogWarning(ex, "Could not embed product {ProductId}; leaving embedding empty.", product.Id);
        }
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchSemanticAsync(
        string query, int take, CancellationToken cancellationToken = default)
    {
        // 1) Sanitize inputs. Empty query -> 400. Clamp 'take' to a sane 1..50.
        if (string.IsNullOrWhiteSpace(query))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["q"] = new[] { "A search query is required." }
            });
        take = Math.Clamp(take, 1, 50);

        // 2) Embed the QUERY. A failure here is fatal for search (no fallback) -> propagates as 502.
        var queryVector = new Pgvector.Vector(await _embeddings.EmbedAsync(query, cancellationToken));

        // 3) Rank in the DATABASE using cosine distance (<=>). Only APPROVED stores are public,
        //    and only products that actually have an embedding can be ranked. Smaller = closer.
        //    The distance is projected ONCE (into 'Distance') and the ordering reuses that
        //    projection, so Postgres computes 'embedding <=> query' a single time per row.
        var hits = await _db.Products
            .Where(p => p.Store.Status == StoreStatus.Approved && p.Embedding != null)
            .Select(p => new
            {
                Response = new ProductResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Sku = p.Sku,
                    Price = p.Price,
                    ImageUrl = p.ImageUrl,
                    CreatedAt = p.CreatedAt,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category.Name,
                    StoreId = p.StoreId,
                    StoreName = p.Store.Name,
                    StoreSlug = p.Store.Slug,
                    QuantityInStock = p.Inventory != null ? p.Inventory.QuantityInStock : 0,
                    AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                    ReviewCount = p.Reviews.Count,
                    SeoTitle = p.SeoTitle,
                    MetaDescription = p.MetaDescription,
                    FeaturesJson = p.FeaturesJson
                },
                Distance = p.Embedding!.CosineDistance(queryVector)
            })
            .OrderBy(x => x.Distance)
            .Take(take)
            .ToListAsync(cancellationToken);

        var results = hits.Select(h => new SemanticSearchResult(h.Response, h.Distance)).ToList();
        PopulateFeatures(results.Select(r => r.Product));   // reuse the existing FeaturesJson decoder
        return results;
    }

    // Max products embedded in a single backfill call, so one HTTP request can't run unboundedly
    // over a huge catalogue (a sequence of paid, throttled AI calls). The endpoint is idempotent —
    // callers re-run it until Response.Remaining == 0.
    private const int MaxBackfillBatch = 500;

    public async Task<BackfillEmbeddingsResponse> BackfillEmbeddingsAsync(
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Clamp(batchSize, 1, MaxBackfillBatch);

        // Only products missing an embedding, capped at batchSize so the request is bounded.
        // The soft-delete global filter already excludes deleted products (not searchable anyway).
        // Deterministic order (by Id) so repeated runs make steady forward progress.
        var pending = await _db.Products
            .Include(p => p.Category)
            .Where(p => p.Embedding == null)
            .OrderBy(p => p.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        int embedded = 0, failed = 0;
        foreach (var product in pending)
        {
            try
            {
                var text = $"{product.Name}\n{product.Description}\n{product.Category.Name}";
                product.Embedding = new Pgvector.Vector(await _embeddings.EmbedAsync(text, cancellationToken));
                await _db.SaveChangesAsync(cancellationToken);
                embedded++;
                await Task.Delay(200, cancellationToken);   // gentle throttle to stay under the rate limit
            }
            catch (ExternalServiceException ex)
            {
                failed++;
                _logger.LogWarning(ex, "Backfill: failed to embed product {ProductId}; will retry on next run.", product.Id);
            }
        }

        // How many products STILL have no embedding after this batch (includes this batch's
        // failures). When this reaches 0, the backfill is complete.
        var remaining = await _db.Products.CountAsync(p => p.Embedding == null, cancellationToken);
        _logger.LogInformation(
            "Backfill embedded {Embedded}, failed {Failed}, {Remaining} product(s) still pending.",
            embedded, failed, remaining);
        return new BackfillEmbeddingsResponse(embedded, failed, remaining);
    }

    // Pure cosine similarity of two equal-length vectors: 1 = identical direction, 0 = orthogonal.
    // NOTE: this helper is NOT used by the production ranking path — SearchSemanticAsync ranks in
    // Postgres via the pgvector <=> operator (cosine DISTANCE = 1 - similarity), which the in-memory
    // EF provider cannot execute. It exists ONLY so unit tests can assert ranking order on canned
    // vectors; the real SQL ordering is covered by manual verification. Keep the two in sync: a
    // smaller <=> distance corresponds to a larger similarity here. Returns 0 for a zero vector.
    internal static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length.");

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            magA += (double)a[i] * a[i];
            magB += (double)b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    public async Task DeleteAsync(int id)
    {
        // Load the tracked entity (the global filter means this finds only NOT-deleted ones).
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

        // Ownership (TB-125): only the owning seller (or an admin) may delete this product.
        await EnsureCanManageStoreAsync(product.StoreId);

        // Soft delete: flip the flag instead of removing the row.
        product.IsDeleted = true;

        // EF sends a single UPDATE setting IsDeleted = 1.
        await _db.SaveChangesAsync();

        // --- INVALIDATE: a deleted product must vanish from caches too.
        await _cache.InvalidateProductAsync(id);
    }

    public async Task<string> SetProductImageAsync(int id, IFormFile file)
    {
        // 1) Load the TRACKED product so EF can persist our change to ImageUrl.
        //    The global query filter means a soft-deleted product won't be found.
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

        // Ownership (TB-125): only the owning seller (or an admin) may change this image.
        await EnsureCanManageStoreAsync(product.StoreId);

        // 2) Push the file to Azure and get its public URL back.
        //    "product-images" must match the container created in the portal.
        string url = await _blob.UploadAsync(file, "product-images", id);

        // 3) Save the URL onto the product row (one UPDATE).
        product.ImageUrl = url;
        await _db.SaveChangesAsync();

        // 4) INVALIDATE: the product changed, so drop its single cache AND all list caches
        //    (identical to the pattern in UpdateAsync/DeleteAsync).
        await _cache.RemoveByPatternAsync("products:*");
        await _cache.RemoveAsync($"product:{id}");

        return url;
    }

    public async Task RemoveProductImageAsync(int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

        // Ownership (TB-125): only the owning seller (or an admin) may remove this image.
        await EnsureCanManageStoreAsync(product.StoreId);

        // Delete the blob from Azure first (no-op if there's no image / it's already gone).
        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
            await _blob.DeleteAsync(product.ImageUrl);

        // Then clear the database field and save.
        product.ImageUrl = null;
        await _db.SaveChangesAsync();

        // Same cache invalidation as every other product write.
        await _cache.RemoveByPatternAsync("products:*");
        await _cache.RemoveAsync($"product:{id}");
    }

    // ----- Limits that mirror the DATABASE so a bad row is reported, never crashes SaveChanges. -----
    // These match the column definitions on the Product entity (MaxLength attributes) and the
    // decimal(18,2) precision configured in AppDbContext. Validating here turns a would-be
    // SQL "truncation"/"overflow" 500 into a clean per-row error.
    private const int MaxNameLength = 200;          // Product.Name  [MaxLength(200)]
    private const int MaxSkuLength = 50;            // Product.Sku   [MaxLength(50)]
    private const int MaxDescriptionLength = 2000;  // Product.Description [MaxLength(2000)]
    private const decimal MaxPrice = 1_000_000_000m; // sane business cap, far under decimal(18,2) overflow

    public async Task<ImportResultDto> ImportAsync(Stream fileStream)
    {
        // Admin bulk import: every valid row is created in the default store (StoreId = 1) and SKU
        // uniqueness is checked across the WHOLE catalogue. Seller imports use ImportForStoreAsync
        // (store-scoped + ownership-checked) below; both share the helpers underneath.
        return await RunImportAsync(fileStream, storeId: 1, skuScope: null);
    }

    public async Task<ImportResultDto> ImportForStoreAsync(Stream fileStream, int storeId)
    {
        // Ownership FIRST: a seller may only import into a store they own; an admin may target any
        // store. EnsureCanManageStoreAsync throws ForbiddenException (403) otherwise — before we
        // touch the file, so an unauthorised caller learns nothing about the upload.
        await EnsureCanManageStoreAsync(storeId);

        // SKU uniqueness is PER-STORE here (matches the (StoreId, Sku) unique index and CreateAsync),
        // so two different stores may each carry the same SKU.
        return await RunImportAsync(fileStream, storeId, skuScope: storeId);
    }

    // Shared engine for both imports. `storeId` is the store every accepted row is created in;
    // `skuScope` controls which existing SKUs collide — null = the whole catalogue (admin),
    // a value = only that store (seller).
    private async Task<ImportResultDto> RunImportAsync(Stream fileStream, int storeId, int? skuScope)
    {
        var result = new ImportResultDto();

        var categoriesByName = await LoadCategoryIdsByNameAsync();
        var existingSkuSet = await LoadExistingSkusAsync(skuScope);
        var productsToAdd = new List<Product>();

        // A renamed/corrupt/password-protected file passes the controller's ".xlsx" name check but
        // fails HERE; OpenWorkbook turns that into a clean 400 (ValidationException) instead of a 500.
        using var package = OpenWorkbook(fileStream);
        var sheet = package.Workbook.Worksheets.FirstOrDefault();

        // 'Dimension' is null when the sheet is completely empty — nothing to import.
        if (sheet?.Dimension is not null)
            BuildProductsFromSheet(sheet, categoriesByName, existingSkuSet, storeId, productsToAdd, result);

        await SaveImportedProductsAsync(productsToAdd);

        // TB-156: generate a semantic-search embedding for every imported product, exactly like
        // CreateAsync/UpdateAsync do for single products. Without this an imported product's
        // Embedding stays null and it is invisible to semantic search (SearchSemanticAsync filters
        // on Embedding != null). Best-effort per product — a Gemini outage leaves that one product's
        // vector null (the backfill endpoint fills it later) without failing the whole import.
        await EmbedImportedProductsAsync(productsToAdd, categoriesByName);

        result.AddedCount = productsToAdd.Count;
        result.FailedCount = result.Errors.Count;
        return result;
    }

    // Embeds each freshly-inserted product (best-effort, one call per product — same contract as
    // TryUpdateEmbeddingAsync). Rebuilds the id -> name category lookup from the name -> id map the
    // import already loaded, so no extra DB round-trip is needed to get each product's category name.
    private async Task EmbedImportedProductsAsync(List<Product> products, Dictionary<string, int> categoriesByName)
    {
        if (products.Count == 0)
            return;

        var nameByCategoryId = new Dictionary<int, string>();
        foreach (var kvp in categoriesByName)
            nameByCategoryId[kvp.Value] = kvp.Key;

        foreach (var product in products)
        {
            var categoryName = nameByCategoryId.GetValueOrDefault(product.CategoryId, string.Empty);
            await TryUpdateEmbeddingAsync(product, categoryName);
        }
    }

    // Case-INSENSITIVE Category Name -> Id lookup. Uses the indexer (not Add) so duplicate category
    // names in the DB won't throw — the last one simply wins.
    private async Task<Dictionary<string, int>> LoadCategoryIdsByNameAsync()
    {
        var categories = await _db.Categories.AsNoTracking().ToListAsync();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in categories)
            byName[c.Name] = c.Id;
        return byName;
    }

    // The SKUs already taken, so a row can be rejected BEFORE SaveChanges instead of crashing the
    // whole import. Includes soft-deleted rows (the unique index ignores the soft-delete flag).
    // storeId == null => the whole catalogue (admin import); a value => only that store (seller).
    private async Task<HashSet<string>> LoadExistingSkusAsync(int? storeId)
    {
        var query = _db.Products.IgnoreQueryFilters().AsQueryable();
        if (storeId is int sid)
            query = query.Where(p => p.StoreId == sid);
        var skus = await query.Select(p => p.Sku).ToListAsync();
        return new HashSet<string>(skus, StringComparer.OrdinalIgnoreCase);
    }

    // Opens the uploaded stream as an .xlsx workbook, or throws a clean ValidationException (400).
    private static ExcelPackage OpenWorkbook(Stream fileStream)
    {
        try
        {
            return new ExcelPackage(fileStream);
        }
        catch
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[]
                {
                    "The uploaded file could not be opened as a valid .xlsx workbook. " +
                    "It may be corrupt, password-protected, or not a real Excel file."
                }
            });
        }
    }

    // Walks each data row of `sheet` (row 1 = headers), validates it, and either appends a built
    // Product (owned by `storeId`) to `productsToAdd` or one ImportRowError to `result`. Shared by
    // the admin and seller imports — the ONLY differences are the target store and the scope of
    // `existingSkuSet`, both decided by the caller.
    private static void BuildProductsFromSheet(
        ExcelWorksheet sheet,
        Dictionary<string, int> categoriesByName,
        HashSet<string> existingSkuSet,
        int storeId,
        List<Product> productsToAdd,
        ImportResultDto result)
    {
        // Track SKUs we have ALREADY accepted in THIS file, to catch duplicates within the upload.
        var skusSeenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The last used row number. Headers are on row 1, so data starts at row 2.
        int lastRow = sheet.Dimension.End.Row;

        for (int row = 2; row <= lastRow; row++)
        {
            // Read each column as displayed text, then trim surrounding spaces.
            // Column order (from the ticket): 1=Name 2=SKU 3=Price 4=CategoryName 5=Description 6=InitialStock
            string name = sheet.Cells[row, 1].Text.Trim();
            string sku = sheet.Cells[row, 2].Text.Trim();
            string priceText = sheet.Cells[row, 3].Text.Trim();
            string categoryName = sheet.Cells[row, 4].Text.Trim();
            string description = sheet.Cells[row, 5].Text.Trim();
            string stockText = sheet.Cells[row, 6].Text.Trim();

            // Skip a fully blank row (e.g. trailing empty lines) WITHOUT counting it as an error.
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(sku) &&
                string.IsNullOrWhiteSpace(priceText) && string.IsNullOrWhiteSpace(categoryName) &&
                string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(stockText))
            {
                continue;
            }

            // --- VALIDATION: each failed check records ONE reason and skips the row. ---

            // NAME: present and not longer than the DB column.
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = "Name is required." });
                continue;
            }
            if (name.Length > MaxNameLength)
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"Name must be at most {MaxNameLength} characters." });
                continue;
            }

            // SKU: present, within length, not already used in this scope, not duplicated in this file.
            if (string.IsNullOrWhiteSpace(sku))
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = "SKU is required." });
                continue;
            }
            if (sku.Length > MaxSkuLength)
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"SKU must be at most {MaxSkuLength} characters." });
                continue;
            }
            if (existingSkuSet.Contains(sku))
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"SKU '{sku}' already exists." });
                continue;
            }
            if (!skusSeenInFile.Add(sku))   // Add returns false if the SKU was already seen this file.
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"Duplicate SKU '{sku}' appears more than once in the file." });
                continue;
            }

            // PRICE: parse tolerant of locale (try invariant first, then the server's culture),
            //        require > 0, and cap it so it can never overflow the decimal(18,2) column.
            bool priceParsed =
                decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price) ||
                decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.CurrentCulture, out price);
            if (!priceParsed || price <= 0)
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = "Price must be a number greater than 0." });
                continue;
            }
            if (price > MaxPrice)
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"Price must be {MaxPrice:0} or less." });
                continue;
            }

            // CATEGORY: required and must resolve to an existing category id.
            if (string.IsNullOrWhiteSpace(categoryName) ||
                !categoriesByName.TryGetValue(categoryName, out int categoryId))
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"Category '{categoryName}' was not found." });
                continue;
            }

            // DESCRIPTION: optional, but cannot exceed the DB column length.
            if (description.Length > MaxDescriptionLength)
            {
                result.Errors.Add(new ImportRowError { Row = row, Reason = $"Description must be at most {MaxDescriptionLength} characters." });
                continue;
            }

            // INITIAL STOCK: blank => 0; if provided it must be a whole number >= 0.
            int initialStock = 0;
            if (!string.IsNullOrWhiteSpace(stockText))
            {
                if (!int.TryParse(stockText, NumberStyles.Integer, CultureInfo.InvariantCulture, out initialStock) || initialStock < 0)
                {
                    result.Errors.Add(new ImportRowError { Row = row, Reason = "Initial stock must be a whole number of 0 or more." });
                    // Roll back the SKU reservation so it isn't wrongly counted as 'used'.
                    skusSeenInFile.Remove(sku);
                    continue;
                }
            }

            // --- The row passed: build a Product PLUS its one-to-one Inventory record. ---
            // Setting the Inventory navigation lets EF insert BOTH rows together (same as CreateAsync).
            productsToAdd.Add(new Product
            {
                Name = name,
                Description = description,
                Sku = sku,
                Price = price,
                CategoryId = categoryId,
                StoreId = storeId,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
                Inventory = new Inventory { QuantityInStock = initialStock }
            });
        }
    }

    // Inserts all accepted products in ONE transaction. Everything that could fail was filtered out
    // above, so this should never throw; the catch is a last-resort net (e.g. a race where the same
    // SKU is inserted by someone else between our check and our save) that surfaces a clear 409
    // instead of a raw 500 — and, being one transaction, nothing is saved on failure.
    private async Task SaveImportedProductsAsync(List<Product> productsToAdd)
    {
        if (productsToAdd.Count == 0)
            return;

        try
        {
            _db.Products.AddRange(productsToAdd);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(
                "The import could not be saved because of a data conflict (for example a SKU created " +
                "by someone else at the same moment). No products were imported — please try again.");
        }
    }
    // Builds a STABLE, UNIQUE cache key for one specific combination of query parameters.
    // Same parameters -> same key (so the 2nd identical request HITS). Different parameters
    // -> different key (so page 2 never returns page 1's data).
    // The "products:" prefix lets RemoveByPatternAsync("products:*") wipe ALL list caches at once.
    private static string BuildListCacheKey(ProductQueryParameters q)
    {
        // 1) Glue every parameter into one string. The '|' separators keep fields apart so
        //    (Search="a", City="b") can never collide with (Search="ab", City="").
        //    StoreId MUST be included — otherwise a seller-scoped request (StoreId=5) and the
        //    public marketplace's unscoped request (StoreId=null), or another seller's StoreId,
        //    can hash to the SAME key when the rest of the params match, leaking one context's
        //    cached page into a completely different caller.
        string raw =
            $"{q.StoreId}|{q.Search}|{q.CategoryId}|{q.MinPrice}|{q.MaxPrice}|" +
            $"{q.SortBy}|{q.SortDescending}|{q.Page}|{q.PageSize}"; 

        // 2) Hash that string into a short, fixed-length, safe fingerprint (SHA-256).
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        string hash = Convert.ToHexString(bytes); // bytes -> readable hex text

        // 3) Final key, e.g. "products:9F3A2B7C...".
        return $"products:{hash}";
    }


}

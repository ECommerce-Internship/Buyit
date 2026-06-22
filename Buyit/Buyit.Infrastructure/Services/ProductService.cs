using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Http;   // IFormFile (TB-42)
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;   
using OfficeOpenXml;
using System.Globalization;
using System.Security.Cryptography;   
using System.Text;                  
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

/// <summary>The real implementation of IProductService — talks to the database via EF Core.</summary>
public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly IValidator<CreateProductRequest> _createValidator;
    private readonly IValidator<UpdateProductRequest> _updateValidator;
    private readonly ICacheService _cache;
    private readonly IBlobStorageService _blob;   // TB-42: uploads/deletes product images
    private readonly ILogger<ProductService> _logger;

    public ProductService(
     AppDbContext db,
     IValidator<CreateProductRequest> createValidator,
     IValidator<UpdateProductRequest> updateValidator,
     ICacheService cache,
     IBlobStorageService blob,            // <-- new (TB-42)
     ILogger<ProductService> logger)
    {
        _db = db;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _cache = cache;
        _blob = blob;                        // <-- new (TB-42)
        _logger = logger;
    }

    public async Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters query)
    {
        // --- CACHE-ASIDE (read): try the cache before touching the database. ---
        string cacheKey = BuildListCacheKey(query);
        var cached = await _cache.GetAsync<PaginatedResult<ProductResponse>>(cacheKey);
        if (cached is not null)
            return cached;   // HIT: return the saved page; DB is never queried.

        _logger.LogInformation("Querying DATABASE for products list (key {CacheKey})", cacheKey);
        // STAGE 1 — start the query. Nothing runs yet; this is an IQueryable (a plan).
        // The global query filter in AppDbContext already excludes IsDeleted == true.
        IQueryable<Product> products = _db.Products;

        // STAGE 2 — FILTERING. Each filter is applied ONLY if the client supplied it.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Contains => SQL "LIKE '%term%'": product name includes the search text.
            products = products.Where(p => p.Name.Contains(query.Search));
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
                QuantityInStock = p.Inventory != null ? p.Inventory.QuantityInStock : 0,
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                ReviewCount = p.Reviews.Count
            })
            .ToListAsync();   // <-- THE database is hit HERE, exactly once.

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
        var cached = await _cache.GetAsync<ProductResponse>(cacheKey);
        if (cached is not null)
            return cached;   // HIT

        _logger.LogInformation("Querying DATABASE for product {ProductId}", id);

        // Project straight into the DTO; the global filter still hides soft-deleted rows.
        var product = await _db.Products
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
                QuantityInStock = p.Inventory != null ? p.Inventory.QuantityInStock : 0,
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                ReviewCount = p.Reviews.Count
            })
            .FirstOrDefaultAsync();

        // No row matched (wrong id, or the product is soft-deleted) -> 404 via middleware.
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");
        // --- CACHE-ASIDE (write): save this product for 5 minutes. ---
        await _cache.SetAsync(cacheKey, product, TimeSpan.FromMinutes(5));

        return product;
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

        // 2a) The category must actually exist (validator only checked it's > 0).
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
            throw new NotFoundException($"Category with id {request.CategoryId} was not found.");

        // 2b) SKU must be unique -> 409 Conflict if already used.
        //     IgnoreQueryFilters() so even a soft-deleted product's SKU still counts as taken.
        var skuTaken = await _db.Products
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Sku == request.Sku);
        if (skuTaken)
            throw new ConflictException($"A product with SKU '{request.Sku}' already exists.");

        // 3) Build the Product TOGETHER WITH its one-to-one Inventory record.
        //    Setting the Inventory navigation property lets EF insert BOTH rows inside a
        //    SINGLE SaveChanges (one transaction): either both succeed or neither does.
        //    This avoids the previous two-save approach, where the product could be created
        //    but the inventory insert could fail, leaving a product with no stock record.
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Sku = request.Sku,
            Price = request.Price,
            ImageUrl = request.ImageUrl,
            CategoryId = request.CategoryId,
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

        // 3) The new category must exist too.
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
            throw new NotFoundException($"Category with id {request.CategoryId} was not found.");

        // 4) COPY the editable fields. EF marks each changed column 'dirty'.
        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.ImageUrl = request.ImageUrl;
        product.CategoryId = request.CategoryId;

        // 5) One UPDATE statement is sent here.
        await _db.SaveChangesAsync();

        // --- INVALIDATE: the product changed, so drop its single cache AND all list caches.
        await _cache.InvalidateProductAsync(id);

        // 6) Return the updated product in DTO form.
        return await GetByIdAsync(product.Id);
    }
    public async Task DeleteAsync(int id)
    {
        // Load the tracked entity (the global filter means this finds only NOT-deleted ones).
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

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
        // The summary we will fill in and return.
        var result = new ImportResultDto();

        // 1) Load EVERY category once into a case-INSENSITIVE Name -> Id lookup.
        //    Building the dictionary manually (indexer, not Add) means duplicate category
        //    names in the DB won't throw — the last one simply wins. OrdinalIgnoreCase makes
        //    "Books", "books" and "BOOKS" all match without culture surprises.
        var categories = await _db.Categories.AsNoTracking().ToListAsync();
        var categoriesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in categories)
            categoriesByName[c.Name] = c.Id;

        // 1b) Load EVERY existing SKU once (INCLUDING soft-deleted ones, exactly like CreateAsync,
        //     because the unique index ignores the soft-delete flag). This lets us reject a row
        //     whose SKU is already taken — BEFORE SaveChanges — instead of crashing the whole import.
        var existingSkus = await _db.Products
            .IgnoreQueryFilters()
            .Select(p => p.Sku)
            .ToListAsync();
        var existingSkuSet = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);

        // Track SKUs we have ALREADY accepted in THIS file, to catch duplicates within the upload.
        var skusSeenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // We collect only the GOOD products here, then insert them all at the end.
        var productsToAdd = new List<Product>();

        // 2) Open the uploaded stream as an Excel workbook. A renamed/corrupt/password-protected
        //    file passes the controller's ".xlsx" name check but fails HERE — so we catch it and
        //    return a clean 400 (ValidationException) instead of an ugly 500.
        ExcelPackage package;
        try
        {
            package = new ExcelPackage(fileStream);
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

        using (package)
        {
            // The first sheet in the workbook. FirstOrDefault returns null if there are none.
            var sheet = package.Workbook.Worksheets.FirstOrDefault();

            // 'Dimension' is null when the sheet is completely empty (no cells at all).
            if (sheet?.Dimension is null)
            {
                // Nothing to import. AddedCount stays 0, Errors stays empty.
                return result;
            }

            // The last used row number. Headers are on row 1, so data starts at row 2.
            int lastRow = sheet.Dimension.End.Row;

            // 3) Walk every data row.
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

                // SKU: present, within length, not already used in the DB, not duplicated in this file.
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
                    result.Errors.Add(new ImportRowError { Row = row, Reason = $"SKU '{sku}' already exists in the catalogue." });
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
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false,
                    Inventory = new Inventory { QuantityInStock = initialStock }
                });
            }

            // 4) BULK INSERT: queue all good products, then save once (one transaction).
            //    Every known cause of a failed insert was filtered out above, so this should
            //    never throw. The try/catch is a last-resort safety net (e.g. a race where the
            //    same SKU is inserted by someone else between our check and our save): we surface
            //    a clear 409 instead of a raw 500, and — being one transaction — nothing is saved.
            if (productsToAdd.Count > 0)
            {
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

            // 5) Fill in the counts and return the summary.
            result.AddedCount = productsToAdd.Count;
            result.FailedCount = result.Errors.Count;
            return result;
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
        string raw =
            $"{q.Search}|{q.CategoryId}|{q.MinPrice}|{q.MaxPrice}|" +
            $"{q.SortBy}|{q.SortDescending}|{q.Page}|{q.PageSize}";

        // 2) Hash that string into a short, fixed-length, safe fingerprint (SHA-256).
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        string hash = Convert.ToHexString(bytes); // bytes -> readable hex text

        // 3) Final key, e.g. "products:9F3A2B7C...".
        return $"products:{hash}";
    }


}

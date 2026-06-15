using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

/// <summary>The real implementation of IProductService — talks to the database via EF Core.</summary>
public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly IValidator<CreateProductRequest> _createValidator;
    private readonly IValidator<UpdateProductRequest> _updateValidator;

    public ProductService(
        AppDbContext db,
        IValidator<CreateProductRequest> createValidator,
        IValidator<UpdateProductRequest> updateValidator)
    {
        _db = db;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters query)
    {
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
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0
            })
            .ToListAsync();   // <-- THE database is hit HERE, exactly once.

        // Compute total pages = ceil(totalCount / pageSize). We cast to double on purpose so
        // the division keeps its remainder (e.g. 25/10 = 2.5 -> Ceiling -> 3), then back to int.
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        // Assemble and return the page + metadata.
        return new PaginatedResult<ProductResponse>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    public async Task<ProductResponse> GetByIdAsync(int id)
    {
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
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0
            })
            .FirstOrDefaultAsync();

        // No row matched (wrong id, or the product is soft-deleted) -> 404 via middleware.
        if (product is null)
            throw new NotFoundException($"Product with id {id} was not found.");

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
    }


}

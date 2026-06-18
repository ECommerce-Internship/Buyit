using Buyit.Application.DTOs;
using Microsoft.AspNetCore.Http;


namespace Buyit.Application.Interfaces;

/// <summary>
/// The contract for everything you can do with products.
/// Controllers depend on THIS, not on the concrete ProductService.
/// </summary>
public interface IProductService
{
    // Read MANY — paged/filtered/sorted list.
    Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters query);

    // Read ONE by id — throws NotFoundException if it doesn't exist.
    Task<ProductResponse> GetByIdAsync(int id);

    // Create — throws ConflictException if the SKU is already taken.
    Task<ProductResponse> CreateAsync(CreateProductRequest request);

    // Update — throws NotFoundException if the product doesn't exist.
    Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request);

    // Soft delete — sets IsDeleted = true. Throws NotFoundException if missing.
    Task DeleteAsync(int id);

    // Bulk import — reads products from an .xlsx stream, inserts the valid ones,
    // and returns a summary (added count, failed count, per-row errors).
    Task<ImportResultDto> ImportAsync(Stream fileStream);

    // TB-42: set/replace this product's image. Returns the new public image URL.
    Task<string> SetProductImageAsync(int id, IFormFile file);

    // TB-42: remove this product's image (deletes the blob and clears ImageUrl).
    Task RemoveProductImageAsync(int id);
}
using Buyit.Application.DTOs;

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
}
namespace Buyit.Application.DTOs;

public record CategoryResponse(
    int Id,
    string Name,
    string? Description, 
    int? ParentCategoryId,
    int SubcategoryCount,
    IEnumerable<CategoryResponse>? Subcategories = null
);

namespace Buyit.Application.DTOs;

public record UpdateCategoryRequest(
    string Name,
    string? Description, 
    int? ParentCategoryId
);

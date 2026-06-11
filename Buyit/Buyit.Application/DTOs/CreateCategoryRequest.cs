namespace Buyit.Application.DTOs;

public record CreateCategoryRequest(
    string Name,
    string? Description, 
    int? ParentCategoryId
);

using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Microsoft.Extensions.Logging; 

namespace Buyit.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _context;
    private readonly IValidator<CreateCategoryRequest> _createValidator;
    private readonly IValidator<UpdateCategoryRequest> _updateValidator;
    private readonly ILogger<CategoryService> _logger; // <-- 1. ADDED logger field

    public CategoryService(
        AppDbContext context,
        IValidator<CreateCategoryRequest> createValidator,
        IValidator<UpdateCategoryRequest> updateValidator,
        ILogger<CategoryService> logger) // <-- 2. ADDED logger to constructor parameters
    {
        _context = context;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger; // <-- 3. Assigned logger
    }

    // 1. GET ALL: Returns all categories with subcategory count
    public async Task<IEnumerable<CategoryResponse>> GetAllAsync()
    {
        var categories = await _context.Categories
            .Include(c => c.SubCategories)
            .Select(c => new CategoryResponse(
                c.Id,
                c.Name,
                c.Description,
                c.ParentCategoryId,
                c.SubCategories.Count,
                null
            ))
            .ToListAsync();

        return categories;
    }

    // 2. GET BY ID: Returns category with its direct subcategories
    public async Task<CategoryResponse> GetByIdAsync(int id)
    {
        var category = await _context.Categories
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        return new CategoryResponse(
            category.Id,
            category.Name,
            category.Description,
            category.ParentCategoryId,
            category.SubCategories.Count,
            category.SubCategories.Select(sub => new CategoryResponse(
                sub.Id,
                sub.Name,
                sub.Description,
                sub.ParentCategoryId,
                0,
                null
            ))
        );
    }

    // 3. POST: Validates input, checks unique name, saves, returns DTO
    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
        // LOG 1: Track that a creation action was requested by the application layer
        _logger.LogInformation("Processing category creation request for Name: {CategoryName}", request.Name);

        var validationResult = await _createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errorDictionary = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );

            throw new Buyit.Domain.Exceptions.ValidationException(errorDictionary);
        }

        var nameExists = await _context.Categories
            .AnyAsync(c => c.Name.ToLower() == request.Name.ToLower());

        if (nameExists)
            throw new ConflictException($"A category named '{request.Name}' already exists.");

        var category = new Category
        {
            Name = request.Name,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        // LOG 2: Capture a successful database save event along with the fresh structural data
        _logger.LogInformation("Successfully created category {CategoryName} with assigned Database ID: {CategoryId}", category.Name, category.Id);

        return new CategoryResponse(category.Id, category.Name, category.Description, category.ParentCategoryId, 0, null);
    }

    // 4. PUT: Validates input, fetches, updates fields, saves
    public async Task UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var validationResult = await _updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errorDictionary = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );

            throw new Buyit.Domain.Exceptions.ValidationException(errorDictionary);
        }

        var category = await _context.Categories.FindAsync(id);
        if (category == null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        var nameExists = await _context.Categories
            .AnyAsync(c => c.Name.ToLower() == request.Name.ToLower() && c.Id != id);

        if (nameExists)
            throw new ConflictException($"Another category named '{request.Name}' already exists.");

        category.Name = request.Name;
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;

        await _context.SaveChangesAsync();

        // LOG 3: Track successful modifications to data resources
        _logger.LogInformation("Category {CategoryId} details were successfully updated", id);
    }

    // 5. DELETE: Checks no products are linked
    public async Task DeleteAsync(int id)
    {
        var category = await _context.Categories.FindAsync(id);

        if (category == null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
        if (hasProducts)
            throw new ConflictException("Cannot delete category because it has active products linked to it.");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        // LOG 4: A clear audit warning tracking structural data removals
        _logger.LogWarning("Category resource with ID {CategoryId} was permanently deleted from the database", id);
    }
}
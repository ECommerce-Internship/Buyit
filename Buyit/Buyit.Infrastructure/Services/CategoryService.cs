using Microsoft.EntityFrameworkCore;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions; 
using Buyit.Infrastructure.Data;

namespace Buyit.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _context;

    public CategoryService(AppDbContext context)
    {
        _context = context;
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
                null // We don't need nested trees for GetAll unless asked
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
                0, // Leaf node count default
                null
            ))
        );
    }

    // 3. POST: Checks unique name, saves, returns DTO
    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
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

        return new CategoryResponse(category.Id, category.Name, category.Description, category.ParentCategoryId, 0, null);
    }

    // 4. PUT: Fetches, updates fields, saves
    public async Task UpdateAsync(int id, UpdateCategoryRequest request)
    {
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
    }

    // 5. DELETE: Checks no products are linked
    public async Task DeleteAsync(int id)
    {
        var category = await _context.Categories.FindAsync(id);

        if (category == null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        // Check if any products are linked to this category
        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
        if (hasProducts)
            throw new ConflictException("Cannot delete category because it has active products linked to it.");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
    }
}

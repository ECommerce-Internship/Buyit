using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;

namespace Buyit.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]  //this = /api/v1/categories 
public class CategoryController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoryController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    // 1. GET all for api/v1/categories
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _categoryService.GetAllAsync();
        return Ok(categories);
    }

    // 2. GET by id for api/v1/categories/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var category = await _categoryService.GetByIdAsync(id);
        return Ok(category);
    }

    // 3. POST for api/v1/categories but only admin
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        // FluentValidation to handle invalid payloads
        var newCategory = await _categoryService.CreateAsync(request);

        // Returns 201 Created status with location details
        return CreatedAtAction(nameof(GetById), new { id = newCategory.Id }, newCategory);
    }

    // 4. PUT for api/v1/categories/{id} but for admin
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        await _categoryService.UpdateAsync(id, request);
        return NoContent(); // 204 Standard for successful updates
    }

    // 5. DELETE for api/v1/categories/{id} but for admin only
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        await _categoryService.DeleteAsync(id);
        return NoContent(); // 204 Standard for successful deletes
    }
}
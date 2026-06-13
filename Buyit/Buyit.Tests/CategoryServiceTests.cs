using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using FluentValidation.Results;
using Buyit.Application.DTOs;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;

namespace Buyit.Tests;

public class CategoryServiceTests
{
    // Creates a fresh in-memory database and the service instance for each test.
    // Each test has its own isolated database to avoid data leaking between tests.
    private static CategoryService BuildSut(
        out AppDbContext db,
        out Mock<IValidator<CreateCategoryRequest>> createValidatorMock,
        out Mock<IValidator<UpdateCategoryRequest>> updateValidatorMock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);

        // Validators always pass — we're testing service logic, not validation rules
        createValidatorMock = new Mock<IValidator<CreateCategoryRequest>>();
        createValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<CreateCategoryRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        updateValidatorMock = new Mock<IValidator<UpdateCategoryRequest>>();
        updateValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<UpdateCategoryRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        return new CategoryService(db, createValidatorMock.Object, updateValidatorMock.Object);
    }

    [Fact]
    public async Task CreateCategory_ValidRequest_ReturnsCategory()
    {
        var sut = BuildSut(out var db, out _, out _);
        var request = new CreateCategoryRequest("Electronics", "Gadgets and Devices", null);

        CategoryResponse result = await sut.CreateAsync(request);

        // Verify the returned DTO has the correct data
        result.Should().NotBeNull();
        result.Name.Should().Be(request.Name);
        result.Description.Should().Be(request.Description);

        // Verify the category was actually persisted to the database
        var savedCategory = await db.Categories.FirstOrDefaultAsync(c => c.Id == result.Id);
        savedCategory.Should().NotBeNull();
        savedCategory!.Name.Should().Be(request.Name);
    }

    [Fact]
    public async Task CreateCategory_DuplicateName_ThrowsConflictException()
    {
        var sut = BuildSut(out var db, out _, out _);

        // Seed an existing category so the duplicate check has something to find
        db.Categories.Add(new Category { Id = 10, Name = "Electronics", Description = "Old" });
        await db.SaveChangesAsync();

        // Use a different casing to confirm the duplicate check is case-insensitive
        var request = new CreateCategoryRequest("electronics", null, null);

        Func<Task> act = async () => await sut.CreateAsync(request);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task GetCategoryById_ExistingId_ReturnsCategoryWithSubcategories()
    {
        var sut = BuildSut(out var db, out _, out _);

        // Seed a parent category and a subcategory linked to it via ParentCategoryId
        db.Categories.Add(new Category { Id = 1, Name = "Computers", Description = "Main category" });
        db.Categories.Add(new Category { Id = 2, Name = "Laptops", Description = "Sub category", ParentCategoryId = 1 });
        await db.SaveChangesAsync();

        CategoryResponse result = await sut.GetByIdAsync(1);

        result.Should().NotBeNull();
        result.Id.Should().Be(1);

        // Verify the subcategory is included in the response
        result.Subcategories.Should().NotBeNull();
        result.Subcategories!.Count().Should().Be(1);
        result.Subcategories!.First().Name.Should().Be("Laptops");
    }

    [Fact]
    public async Task GetCategoryById_NotFound_ThrowsNotFoundException()
    {
        var sut = BuildSut(out var db, out _, out _);

        // No data seeded — any ID lookup should fail
        Func<Task> act = async () => await sut.GetByIdAsync(999);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*was not found*");
    }

    [Fact]
    public async Task DeleteCategory_WithLinkedProducts_ThrowsConflictException()
    {
        var sut = BuildSut(out var db, out _, out _);

        // Seed a category with a linked product to trigger the conflict guard
        db.Categories.Add(new Category { Id = 5, Name = "Home Appliances" });
        db.Products.Add(new Product
        {
            Id = 1,
            Name = "Microwave",
            Description = "A kitchen appliance etc...",
            Sku = "MIC-001",
            Price = 79.99m,
            CategoryId = 5
        });
        await db.SaveChangesAsync();

        Func<Task> act = async () => await sut.DeleteAsync(5);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*active products linked to it*");

        // Confirm the category was not deleted after the exception
        var categoryStillExists = await db.Categories.AnyAsync(c => c.Id == 5);
        categoryStillExists.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCategory_NoLinkedProducts_DeletesSuccessfully()
    {
        var sut = BuildSut(out var db, out _, out _);

        // Seed a category with no products so deletion should succeed
        db.Categories.Add(new Category { Id = 5, Name = "Empty Category" });
        await db.SaveChangesAsync();

        await sut.DeleteAsync(5);

        // Confirm the category was removed from the database
        var categoryExists = await db.Categories.AnyAsync(c => c.Id == 5);
        categoryExists.Should().BeFalse();
    }
}
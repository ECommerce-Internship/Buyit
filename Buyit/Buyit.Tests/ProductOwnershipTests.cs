using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Buyit.Tests;

// TB-125 ownership: a seller may mutate only products in their OWN store; admin bypasses.
public class ProductOwnershipTests
{
    // Builds a ProductService whose "current user" is the given seller (not admin),
    // unless isAdmin is set.
    private static ProductService BuildSut(out AppDbContext db, int callerUserId, bool isAdmin)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new AppDbContext(options);

        var createV = new Mock<IValidator<CreateProductRequest>>();
        createV.Setup(v => v.ValidateAsync(It.IsAny<CreateProductRequest>(), default)).ReturnsAsync(new ValidationResult());
        var updateV = new Mock<IValidator<UpdateProductRequest>>();
        updateV.Setup(v => v.ValidateAsync(It.IsAny<UpdateProductRequest>(), default)).ReturnsAsync(new ValidationResult());

        var current = new Mock<ICurrentUserService>();
        current.Setup(c => c.IsAdmin).Returns(isAdmin);
        current.Setup(c => c.UserId).Returns(callerUserId);

        // TB-47: not exercised by ownership tests, but required by the ProductService constructor.
        var gemini = new Mock<IGeminiService>();
        var generateContentV = new Mock<IValidator<GenerateContentRequest>>();
        generateContentV.Setup(v => v.ValidateAsync(It.IsAny<GenerateContentRequest>(), default)).ReturnsAsync(new ValidationResult());

        // TB-156: embedder mocked with a valid 768-vector so best-effort embedding on create/update
        // succeeds silently; ownership tests don't assert on embeddings.
        var embeddings = new Mock<IEmbeddingService>();
        embeddings.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        return new ProductService(
            db, createV.Object, updateV.Object,
            new Mock<ICacheService>().Object, new Mock<IBlobStorageService>().Object,
            current.Object, gemini.Object, generateContentV.Object,
            embeddings.Object,
            new Mock<ILogger<ProductService>>().Object);
    }

    // Seeds a category and two stores (owners A and B), each with one product.
    private static async Task<(int catId, int storeAProductId, int storeBProductId)> SeedAsync(AppDbContext db)
    {
        var category = new Category { Name = "Electronics" };
        db.Categories.Add(category);
        var storeA = new Store { OwnerUserId = 10, Name = "A", Slug = "a", Status = StoreStatus.Approved };
        var storeB = new Store { OwnerUserId = 20, Name = "B", Slug = "b", Status = StoreStatus.Approved };
        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        var pA = new Product { Name = "A-prod", Description = "d", Sku = "A-1", Price = 5m, Category = category, StoreId = storeA.Id };
        var pB = new Product { Name = "B-prod", Description = "d", Sku = "B-1", Price = 5m, Category = category, StoreId = storeB.Id };
        db.Products.AddRange(pA, pB);
        await db.SaveChangesAsync();

        return (category.Id, pA.Id, pB.Id);
    }

    private static UpdateProductRequest UpdateReq(int catId) => new()
    {
        Name = "Renamed", Description = "d", Price = 9m, CategoryId = catId
    };

    [Fact]
    public async Task Update_OtherSellersProduct_ThrowsForbidden()
    {
        // Caller owns store A (user 10); tries to edit store B's product.
        var sut = BuildSut(out var db, callerUserId: 10, isAdmin: false);
        var (catId, _, storeBProductId) = await SeedAsync(db);

        Func<Task> act = async () => await sut.UpdateAsync(storeBProductId, UpdateReq(catId));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Update_OwnProduct_Succeeds()
    {
        var sut = BuildSut(out var db, callerUserId: 10, isAdmin: false);
        var (catId, storeAProductId, _) = await SeedAsync(db);

        var result = await sut.UpdateAsync(storeAProductId, UpdateReq(catId));

        result.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Delete_OtherSellersProduct_ThrowsForbidden()
    {
        var sut = BuildSut(out var db, callerUserId: 10, isAdmin: false);
        var (_, _, storeBProductId) = await SeedAsync(db);

        Func<Task> act = async () => await sut.DeleteAsync(storeBProductId);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Admin_CanUpdateAnyProduct()
    {
        // Admin bypasses ownership entirely.
        var sut = BuildSut(out var db, callerUserId: 999, isAdmin: true);
        var (catId, _, storeBProductId) = await SeedAsync(db);

        var result = await sut.UpdateAsync(storeBProductId, UpdateReq(catId));

        result.Name.Should().Be("Renamed");
    }
}

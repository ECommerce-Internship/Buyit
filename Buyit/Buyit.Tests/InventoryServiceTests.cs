using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;

namespace Buyit.Tests;

public class InventoryServiceTests
{
    // GetLowStockAsync uses only the DbContext; the other dependencies are unused for this path,
    // so a bare context + default mocks are enough (see §6.4 BuildSut style).
    private static InventoryService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);
        return new InventoryService(
            db,
            new Mock<ILowStockAlertService>().Object,
            new Mock<ICacheService>().Object,
            new Mock<ICurrentUserService>().Object);
    }

    // Seeds a store owned by ownerUserId with one LOW-stock product (qty <= threshold) and one
    // healthy product. Returns the low-stock product's name so tests can assert on it.
    private static async Task<string> SeedStoreWithLowStockAsync(AppDbContext db, int ownerUserId)
    {
        var category = new Category { Name = $"Cat-{Guid.NewGuid()}" };
        db.Categories.Add(category);

        var store = new Store
        {
            Name = $"Store-{ownerUserId}",
            Slug = $"store-{Guid.NewGuid()}",
            Status = StoreStatus.Approved,
            OwnerUserId = ownerUserId
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var lowName = $"Low-{ownerUserId}";
        db.Products.AddRange(
            new Product
            {
                Name = lowName,
                Description = "low",
                Sku = $"SKU-{Guid.NewGuid()}",
                Price = 9.99m,
                CategoryId = category.Id,
                StoreId = store.Id,
                Inventory = new Inventory { QuantityInStock = 1, LowStockThreshold = 5 }
            },
            new Product
            {
                Name = $"Healthy-{ownerUserId}",
                Description = "healthy",
                Sku = $"SKU-{Guid.NewGuid()}",
                Price = 9.99m,
                CategoryId = category.Id,
                StoreId = store.Id,
                Inventory = new Inventory { QuantityInStock = 50, LowStockThreshold = 5 }
            });
        await db.SaveChangesAsync();
        return lowName;
    }

    [Fact]
    public async Task GetLowStockAsync_ScopedToSeller_ReturnsOnlyThatSellersLowStock()
    {
        var sut = BuildSut(out var db);
        var sellerLow = await SeedStoreWithLowStockAsync(db, ownerUserId: 7);
        await SeedStoreWithLowStockAsync(db, ownerUserId: 8);   // another seller — must not leak

        var result = await sut.GetLowStockAsync(sellerUserId: 7);

        result.Should().ContainSingle()
              .Which.ProductName.Should().Be(sellerLow);
    }

    [Fact]
    public async Task GetLowStockAsync_NoSeller_ReturnsPlatformWideLowStock()
    {
        var sut = BuildSut(out var db);
        await SeedStoreWithLowStockAsync(db, ownerUserId: 7);
        await SeedStoreWithLowStockAsync(db, ownerUserId: 8);

        var result = await sut.GetLowStockAsync();   // admin / unscoped

        // One low-stock product per store, both returned.
        result.Should().HaveCount(2);
    }
}

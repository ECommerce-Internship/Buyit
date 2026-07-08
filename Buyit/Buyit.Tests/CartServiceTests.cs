using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;

namespace Buyit.Tests;

public class CartServiceTests
{
    // CartService needs AppDbContext 
    private static CartService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);
        return new CartService(db);
    }

    // Seeds a user and returns their userId
    private static async Task<int> SeedUserAsync(AppDbContext db)
    {
        var user = new User
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@buyit.com",
            PasswordHash = "irrelevant",
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    // Seeds a product with inventory and returns the product
    private static async Task<Product> SeedProductAsync(AppDbContext db, int stock)
    {
        db.Categories.Add(new Category { Id = 1, Name = "Test Category" });

        // Every product requires a Store in real data (ProductService.CreateAsync always sets
        // one) — seed one here too, otherwise Product.Store stays null and BuildCartResponse's
        // ci.Product.Store.Name throws a NullReferenceException that could never happen for real.
        var store = new Store
        {
            Name = "Test Store",
            Slug = $"test-store-{Guid.NewGuid()}",
            Status = StoreStatus.Approved,
            OwnerUserId = 1
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "Wireless Mouse",
            Description = "A test product",
            Sku = $"SKU-{Guid.NewGuid()}",
            Price = 19.99m,
            CategoryId = 1,
            StoreId = store.Id,
            Inventory = new Inventory { QuantityInStock = stock, LowStockThreshold = 5 }
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    // TEST 1: Adding an item with sufficient stock adds it to the cart
    [Fact]
    public async Task AddToCart_SufficientStock_AddsCartItem()
    {
        var sut = BuildSut(out var db);
        var userId = await SeedUserAsync(db);
        var product = await SeedProductAsync(db, stock: 10);

        var result = await sut.AddItemAsync(userId, new AddCartItemRequest(product.Id, 2));

        result.Items.Should().HaveCount(1);
        result.Items.First().ProductId.Should().Be(product.Id);
        result.Items.First().Quantity.Should().Be(2);

        // Verify cart item was persisted to the database
        var cartItem = await db.CartItems.FirstOrDefaultAsync();
        cartItem.Should().NotBeNull();
        cartItem!.Quantity.Should().Be(2);
    }

    // TEST 2: Adding an item with insufficient stock throws ValidationException with product name
    [Fact]
    public async Task AddToCart_InsufficientStock_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var userId = await SeedUserAsync(db);

        // Only 2 in stock, requesting 5
        var product = await SeedProductAsync(db, stock: 2);

        Func<Task> act = async () =>
            await sut.AddItemAsync(userId, new AddCartItemRequest(product.Id, 5));

        var exception = await act.Should().ThrowAsync<ValidationException>();

        // Assert exception message contains the product name
        exception.Which.Errors.Values
        .SelectMany(e => e)
        .Should().Contain(m => m.Contains("Wireless Mouse"));
    }

    // TEST 3: A zero or negative quantity is rejected and nothing is persisted.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddToCart_NonPositiveQuantity_ThrowsAndPersistsNothing(int quantity)
    {
        var sut = BuildSut(out var db);
        var userId = await SeedUserAsync(db);
        var product = await SeedProductAsync(db, stock: 10);

        Func<Task> act = async () =>
            await sut.AddItemAsync(userId, new AddCartItemRequest(product.Id, quantity));

        await act.Should().ThrowAsync<ValidationException>();
        (await db.CartItems.AnyAsync()).Should().BeFalse();   // no corrupt line persisted
    }
}

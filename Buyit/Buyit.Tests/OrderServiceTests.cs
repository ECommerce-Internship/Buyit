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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Tests;

public class OrderServiceTests
{
    // OrderService needs AppDbContext + mocked email service + mocked validators
    private static OrderService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

        db = new AppDbContext(options);

        // Email service is fire-and-forget 
        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendOrderConfirmationAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>()))
            .Returns(Task.CompletedTask);

        // Validators always pass
        var placeOrderValidatorMock = new Mock<IValidator<PlaceOrderRequest>>();
        placeOrderValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<PlaceOrderRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        var updateStatusValidatorMock = new Mock<IValidator<UpdateOrderStatusRequest>>();
        updateStatusValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<UpdateOrderStatusRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        var cacheMock = new Mock<ICacheService>();

        return new OrderService(
            db,
            emailMock.Object,
            placeOrderValidatorMock.Object,
            cacheMock.Object,
            updateStatusValidatorMock.Object);
    }

    // Seeds a user, product with inventory, and an empty cart
    private static async Task<(int userId, Cart cart, Product product)> SeedOrderDataAsync(
        AppDbContext db, int stock = 50)
    {
        var user = new User
        {
            FirstName = "Order",
            LastName = "User",
            Email = "order@buyit.com",
            PasswordHash = "irrelevant",
            Role = UserRole.Customer
        };
        db.Users.Add(user);

        var category = new Category { Name = "Electronics" };
        db.Categories.Add(category);

        var product = new Product
        {
            Name = "Laptop",
            Description = "A test laptop",
            Sku = $"LAP-{Guid.NewGuid()}",
            Price = 1200.00m,
            Category = category,
            Inventory = new Inventory { QuantityInStock = stock, LowStockThreshold = 5 }
        };
        db.Products.Add(product);

        await db.SaveChangesAsync();

        var cart = new Cart { UserId = user.Id };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        return (user.Id, cart, product);
    }

    // Shipping address
    private static PlaceOrderRequest ValidShippingRequest() => new(
        "verdun, abc mall street etc..",
        null,
        "Beirut",
        "1100",
        "Lebanon"
    );

    // TEST 5: Placing a valid order creates the order, deducts inventory, clears cart
    [Fact]
    public async Task PlaceOrder_ValidCart_CreatesOrderAndDeductsInventory()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product) = await SeedOrderDataAsync(db, stock: 50);

        // Add item to cart
        db.CartItems.Add(new CartItem
        {
            CartId = cart.Id,
            ProductId = product.Id,
            Quantity = 3
        });
        await db.SaveChangesAsync();

        var result = await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        // Assert order created with correct total
        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(product.Price * 3);
        result.Status.Should().Be("Pending");
        result.Items.Should().HaveCount(1);

        // Assert inventory was decremented
        var inventory = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == product.Id);
        inventory!.QuantityInStock.Should().Be(47); // 50 - 3

        // Assert cart was cleared
        var cartItems = await db.CartItems.Where(ci => ci.CartId == cart.Id).ToListAsync();
        cartItems.Should().BeEmpty();
    }

    // TEST 6: Placing an order with an empty cart throws ValidationException
    [Fact]
    public async Task PlaceOrder_EmptyCart_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, _, _) = await SeedOrderDataAsync(db);

        // Cart exists but has no items
        Func<Task> act = async () =>
            await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Values
            .SelectMany(e => e)
            .Should().Contain(m => m.Contains("empty"));
    }

    // TEST 7: Placing an order with out-of-stock item throws ValidationException with product name
    [Fact]
    public async Task PlaceOrder_ItemOutOfStock_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product) = await SeedOrderDataAsync(db, stock: 1);

        // Request more than available stock
        db.CartItems.Add(new CartItem
        {
            CartId = cart.Id,
            ProductId = product.Id,
            Quantity = 10
        });
        await db.SaveChangesAsync();

        Func<Task> act = async () =>
            await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var exception = await act.Should().ThrowAsync<ValidationException>();

        // Assert exception message contains the product name
        exception.Which.Errors.Values
            .SelectMany(e => e)
            .Should().Contain(m => m.Contains("Laptop"));
    }

    // TEST 8: Cancelling a Pending order sets status to Cancelled
    [Fact]
    public async Task CancelOrder_PendingStatus_SetsStatusCancelled()
    {
        var sut = BuildSut(out var db);
        var (userId, _, _) = await SeedOrderDataAsync(db);

        // Seed a pending order
        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Pending,
            TotalAmount = 100,
            ShippingLine1 = "verdun, abc mall street etc..",
            ShippingCity = "Beirut",
            ShippingPostalCode = "1100",
            ShippingCountry = "Lebanon"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await sut.CancelOrderAsync(order.Id, userId);

        var updated = await db.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);
    }

    // TEST 9: Cancelling a non-Pending order throws ValidationException
    [Fact]
    public async Task CancelOrder_NonPendingStatus_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, _, _) = await SeedOrderDataAsync(db);

        // Seed a confirmed order 
        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Confirmed,
            TotalAmount = 100,
            ShippingLine1 = "verdun, abc mall street etc..",
            ShippingCity = "Beirut",
            ShippingPostalCode = "1100",
            ShippingCountry = "Lebanon"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        Func<Task> act = async () => await sut.CancelOrderAsync(order.Id, userId);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Values
            .SelectMany(e => e)
            .Should().Contain(m => m.Contains("Confirmed"));
    }
}
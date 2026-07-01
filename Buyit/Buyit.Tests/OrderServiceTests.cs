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
    private static OrderService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

        db = new AppDbContext(options);

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendOrderConfirmationAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>()))
            .Returns(Task.CompletedTask);

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

    // Seeds a buyer, a seller-owned approved store (10% commission), a product with stock, and an empty cart.
    private static async Task<(int userId, Cart cart, Product product, int storeId)> SeedOrderDataAsync(
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

        var seller = new User
        {
            FirstName = "Seller",
            LastName = "User",
            Email = $"seller-{Guid.NewGuid()}@buyit.com",
            PasswordHash = "irrelevant",
            Role = UserRole.Seller
        };
        db.Users.Add(seller);
        await db.SaveChangesAsync();

        var store = new Store
        {
            OwnerUserId = seller.Id,
            Name = "Test Store",
            Slug = $"test-store-{Guid.NewGuid()}",
            Status = StoreStatus.Approved,
            CommissionRate = 0.10m
        };
        db.Stores.Add(store);

        var category = new Category { Name = "Electronics" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "Laptop",
            Description = "A test laptop",
            Sku = $"LAP-{Guid.NewGuid()}",
            Price = 1200.00m,
            Category = category,
            StoreId = store.Id,
            Inventory = new Inventory { QuantityInStock = stock, LowStockThreshold = 5 }
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var cart = new Cart { UserId = user.Id };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        return (user.Id, cart, product, store.Id);
    }

    private static PlaceOrderRequest ValidShippingRequest() => new(
        "verdun, abc mall street etc..",
        null,
        "Beirut",
        "Mount Lebanon",
        "1100",
        "Lebanon"
    );

    [Fact]
    public async Task GetAllOrders_IncludesCustomerEmail()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);
        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 1 });
        await db.SaveChangesAsync();
        await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var page = await sut.GetAllOrdersAsync(1, 10, status: null, from: null, to: null);

        page.Items.Should().NotBeEmpty();
        page.Items.First().CustomerEmail.Should().Be("order@buyit.com");
    }

    [Fact]
    public async Task UpdateOrderStatus_ValidTransition_UpdatesAllStoreSlices()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);
        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 2 });
        await db.SaveChangesAsync();
        var placed = await sut.PlaceOrderAsync(userId, ValidShippingRequest());   // all slices Pending

        var result = await sut.UpdateOrderStatusAsync(placed.OrderId, new UpdateOrderStatusRequest("Confirmed"));

        result.Status.Should().Be("Confirmed");
        var slices = await db.StoreOrders.Where(so => so.OrderId == placed.OrderId).ToListAsync();
        slices.Should().OnlyContain(so => so.Status == OrderStatus.Confirmed);
    }

    [Fact]
    public async Task UpdateOrderStatus_IllegalTransition_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);
        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 2 });
        await db.SaveChangesAsync();
        var placed = await sut.PlaceOrderAsync(userId, ValidShippingRequest());   // slices Pending

        // Pending -> Delivered is NOT a legal single hop (ValidProgressions), so it must be rejected.
        Func<Task> act = async () =>
            await sut.UpdateOrderStatusAsync(placed.OrderId, new UpdateOrderStatusRequest("Delivered"));

        await act.Should().ThrowAsync<Buyit.Domain.Exceptions.ValidationException>();
    }

    [Fact]
    public async Task PlaceOrder_ValidCart_FansOutAndDeductsInventory()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);

        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 3 });
        await db.SaveChangesAsync();

        var result = await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(product.Price * 3);   // 3600, no coupon
        result.Status.Should().Be("Pending");
        result.StoreOrders.Should().HaveCount(1);            // single store -> one StoreOrder
        result.StoreOrders.First().Items.Should().HaveCount(1);

        var inventory = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == product.Id);
        inventory!.QuantityInStock.Should().Be(47);          // 50 - 3

        var cartItems = await db.CartItems.Where(ci => ci.CartId == cart.Id).ToListAsync();
        cartItems.Should().BeEmpty();
    }

    [Fact]
    public async Task PlaceOrder_ValidCart_ComputesCommissionAndNet()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);

        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 3 });
        await db.SaveChangesAsync();

        var result = await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var storeOrder = result.StoreOrders.First();
        storeOrder.SubTotal.Should().Be(3600.00m);            // 1200 * 3
        storeOrder.CommissionAmount.Should().Be(360.00m);     // 10% of 3600
        storeOrder.SellerNetAmount.Should().Be(3240.00m);     // 3600 - 360
    }

    [Fact]
    public async Task PlaceOrder_EmptyCart_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, _, _, _) = await SeedOrderDataAsync(db);

        Func<Task> act = async () => await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Values.SelectMany(e => e)
            .Should().Contain(m => m.Contains("empty"));
    }

    [Fact]
    public async Task PlaceOrder_ItemOutOfStock_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 1);

        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 10 });
        await db.SaveChangesAsync();

        Func<Task> act = async () => await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Values.SelectMany(e => e)
            .Should().Contain(m => m.Contains("Laptop"));
    }

    [Fact]
    public async Task CancelStoreOrder_PendingStatus_SetsStatusCancelledAndRestocks()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);

        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 3 });
        await db.SaveChangesAsync();
        await sut.PlaceOrderAsync(userId, ValidShippingRequest());   // creates a Pending StoreOrder, stock -> 47

        var storeOrder = await db.StoreOrders.FirstAsync();

        await sut.CancelStoreOrderAsync(storeOrder.Id, userId, isAdmin: false);

        var updated = await db.StoreOrders.FindAsync(storeOrder.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);

        var inventory = await db.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityInStock.Should().Be(50);   // 47 restocked back to 50
    }

    [Fact]
    public async Task CancelStoreOrder_NonPendingStatus_ThrowsValidationException()
    {
        var sut = BuildSut(out var db);
        var (userId, cart, product, _) = await SeedOrderDataAsync(db, stock: 50);

        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Quantity = 3 });
        await db.SaveChangesAsync();
        await sut.PlaceOrderAsync(userId, ValidShippingRequest());

        var storeOrder = await db.StoreOrders.FirstAsync();
        storeOrder.Status = OrderStatus.Confirmed;   // move it past Pending
        await db.SaveChangesAsync();

        Func<Task> act = async () => await sut.CancelStoreOrderAsync(storeOrder.Id, userId, isAdmin: false);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Values.SelectMany(e => e)
            .Should().Contain(m => m.Contains("Confirmed"));
    }
}

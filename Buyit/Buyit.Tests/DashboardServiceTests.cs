using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Buyit.Tests;

// TB-129/TB-48: marketplace-aware dashboards. Admin sees platform-wide + commission;
// sellers see only their own store's numbers.
public class DashboardServiceTests
{
    private const int SellerA = 10;
    private const int SellerB = 20;

    private static DashboardService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new AppDbContext(options);
        // Cache mock: Moq returns a completed Task with default(T) for GetAsync -> a cache MISS,
        // so every call exercises the real DB path; SetAsync is a no-op.
        return new DashboardService(db, new Mock<ICacheService>().Object);
    }

    // One paid order ($100) split across store A (sub 60, commission 6) and store B (sub 40, commission 4).
    private static async Task SeedAsync(AppDbContext db)
    {
        var customer = new User
        {
            FirstName = "C", LastName = "U", Email = $"c-{Guid.NewGuid()}@x.com",
            PasswordHash = "x", Role = UserRole.Customer
        };
        db.Users.Add(customer);

        var storeA = new Store { OwnerUserId = SellerA, Name = "A", Slug = "a", Status = StoreStatus.Approved };
        var storeB = new Store { OwnerUserId = SellerB, Name = "B", Slug = "b", Status = StoreStatus.Approved };
        db.Stores.AddRange(storeA, storeB);
        var category = new Category { Name = "Cat" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var pA = new Product { Name = "A-prod", Description = "d", Sku = "A-1", Price = 60m, Category = category, StoreId = storeA.Id };
        var pB = new Product { Name = "B-prod", Description = "d", Sku = "B-1", Price = 20m, Category = category, StoreId = storeB.Id };
        db.Products.AddRange(pA, pB);
        await db.SaveChangesAsync();

        var order = new Order
        {
            UserId = customer.Id,
            OrderDate = DateTime.UtcNow,
            TotalAmount = 100m,
            ShippingLine1 = "x", ShippingCity = "x", ShippingPostalCode = "0", ShippingCountry = "x",
            Payment = new Payment { Amount = 100m, Method = PaymentMethod.CreditCard, Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow },
            StoreOrders = new List<StoreOrder>
            {
                new()
                {
                    StoreId = storeA.Id, Status = OrderStatus.Pending, SubTotal = 60m, CommissionAmount = 6m, SellerNetAmount = 54m,
                    StoreOrderItems = new List<StoreOrderItem>
                    {
                        new() { ProductId = pA.Id, Quantity = 1, UnitPrice = 60m, ProductNameSnapshot = "A-prod", Subtotal = 60m }
                    }
                },
                new()
                {
                    StoreId = storeB.Id, Status = OrderStatus.Pending, SubTotal = 40m, CommissionAmount = 4m, SellerNetAmount = 36m,
                    StoreOrderItems = new List<StoreOrderItem>
                    {
                        new() { ProductId = pB.Id, Quantity = 2, UnitPrice = 20m, ProductNameSnapshot = "B-prod", Subtotal = 40m }
                    }
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSummary_Admin_IncludesRevenueAndTotalCommission()
    {
        var sut = BuildSut(out var db);
        await SeedAsync(db);

        var summary = await sut.GetSummaryAsync(null);

        summary.TotalRevenue.Should().Be(100m);       // one paid payment
        summary.TotalCommission.Should().Be(10m);      // 6 + 4
        summary.TotalOrders.Should().Be(1);
        summary.TotalCustomers.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_Seller_ScopedToOwnStore()
    {
        var sut = BuildSut(out var db);
        await SeedAsync(db);

        var a = await sut.GetSummaryAsync(SellerA);

        a.TotalRevenue.Should().Be(60m);    // only store A's subtotal
        a.TotalCommission.Should().BeNull(); // commission is an admin-only metric
        a.TotalOrders.Should().Be(1);
    }

    [Fact]
    public async Task GetTopProducts_Seller_OnlyOwnProducts()
    {
        var sut = BuildSut(out var db);
        await SeedAsync(db);

        var adminTop = await sut.GetTopProductsAsync(null);
        var sellerATop = await sut.GetTopProductsAsync(SellerA);

        adminTop.Should().HaveCount(2);                       // both products platform-wide
        sellerATop.Should().ContainSingle()
                  .Which.ProductName.Should().Be("A-prod");   // only their own
    }
}

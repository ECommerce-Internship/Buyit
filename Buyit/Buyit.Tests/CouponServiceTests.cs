using System;
using System.Threading.Tasks;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Application.Validators;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Tests;

public class CouponServiceTests
{
    private static CouponService BuildSut(out AppDbContext db, out Mock<ICurrentUserService> currentUserMock, int? userId = null, bool isAdmin = false)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new AppDbContext(options);

        currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(c => c.UserId).Returns(userId);
        currentUserMock.SetupGet(c => c.IsAdmin).Returns(isAdmin);

        return new CouponService(
            db,
            currentUserMock.Object,
            new CreateCouponRequestValidator(),
            new UpdateCouponRequestValidator());
    }

    private static async Task<Store> SeedStoreAsync(AppDbContext db, int ownerUserId)
    {
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = $"Store {ownerUserId}",
            Slug = $"store-{ownerUserId}-{Guid.NewGuid():N}",
            CommissionRate = 0.1m
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store;
    }

    private static async Task<Coupon> SeedCouponAsync(AppDbContext db, string code, int? storeId, int usageCount = 0, int? usageLimit = null, DateTime? expiryDate = null)
    {
        var coupon = new Coupon
        {
            Code = code,
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = expiryDate ?? DateTime.UtcNow.AddDays(30),
            IsActive = true,
            StoreId = storeId,
            UsageCount = usageCount,
            UsageLimit = usageLimit
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();
        return coupon;
    }

    // ---------- Create: ownership ----------

    [Fact]
    public async Task Create_SellerOwnStore_Succeeds()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);

        var request = new CreateCouponRequest
        {
            Code = "SAVE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            StoreId = store.Id
        };

        var result = await sut.CreateAsync(request);

        result.Code.Should().Be("SAVE10");
        result.StoreId.Should().Be(store.Id);
    }

    [Fact]
    public async Task Create_SellerAnotherStore_ThrowsForbidden()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var otherStore = await SeedStoreAsync(db, ownerUserId: 2);

        var request = new CreateCouponRequest
        {
            Code = "SAVE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            StoreId = otherStore.Id
        };

        var act = () => sut.CreateAsync(request);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Create_SellerGlobalCoupon_ThrowsForbidden()
    {
        var sut = BuildSut(out _, out _, userId: 1, isAdmin: false);

        var request = new CreateCouponRequest
        {
            Code = "GLOBAL10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            StoreId = null
        };

        var act = () => sut.CreateAsync(request);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Create_AdminGlobalCoupon_Succeeds()
    {
        var sut = BuildSut(out _, out _, userId: 99, isAdmin: true);

        var request = new CreateCouponRequest
        {
            Code = "GLOBAL10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            StoreId = null
        };

        var result = await sut.CreateAsync(request);

        result.StoreId.Should().BeNull();
    }

    // ---------- Create: duplicate code ----------

    [Fact]
    public async Task Create_DuplicateCode_ThrowsConflict()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        await SeedCouponAsync(db, "SAVE10", store.Id);

        var request = new CreateCouponRequest
        {
            Code = "save10", // case-insensitive duplicate
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 15,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            StoreId = store.Id
        };

        var act = () => sut.CreateAsync(request);

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ---------- Create: expiry validation ----------

    [Fact]
    public async Task Create_ExpiryInPast_ThrowsValidation()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);

        var request = new CreateCouponRequest
        {
            Code = "EXPIRED",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = DateTime.UtcNow.AddDays(-1),
            StoreId = store.Id
        };

        var act = () => sut.CreateAsync(request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ---------- GetById / Update / Deactivate: not found ----------

    [Fact]
    public async Task GetById_UnknownId_ThrowsNotFound()
    {
        var sut = BuildSut(out _, out _, userId: 1, isAdmin: false);

        var act = () => sut.GetByIdAsync(999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_UnknownId_ThrowsNotFound()
    {
        var sut = BuildSut(out _, out _, userId: 1, isAdmin: false);

        var request = new UpdateCouponRequest
        {
            Code = "X",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 10,
            ExpiryDate = DateTime.UtcNow.AddDays(10)
        };

        var act = () => sut.UpdateAsync(999, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Deactivate_UnknownId_ThrowsNotFound()
    {
        var sut = BuildSut(out _, out _, userId: 1, isAdmin: false);

        var act = () => sut.DeactivateAsync(999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------- Update: ownership ----------

    [Fact]
    public async Task Update_SellerAnotherStoresCoupon_ThrowsForbidden()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var otherStore = await SeedStoreAsync(db, ownerUserId: 2);
        var coupon = await SeedCouponAsync(db, "OTHER10", otherStore.Id);

        var request = new UpdateCouponRequest
        {
            Code = "OTHER10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(10)
        };

        var act = () => sut.UpdateAsync(coupon.Id, request);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Update_SellerOwnStoresCoupon_Succeeds()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id);

        var request = new UpdateCouponRequest
        {
            Code = "MINE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(10)
        };

        var result = await sut.UpdateAsync(coupon.Id, request);

        result.DiscountValue.Should().Be(20);
    }

    [Fact]
    public async Task Update_SellerGlobalCoupon_ThrowsForbidden()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var coupon = await SeedCouponAsync(db, "GLOBAL10", storeId: null);

        var request = new UpdateCouponRequest
        {
            Code = "GLOBAL10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(10)
        };

        var act = () => sut.UpdateAsync(coupon.Id, request);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ---------- Update: duplicate code ----------

    [Fact]
    public async Task Update_ToAnotherCouponsCode_ThrowsConflict()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        await SeedCouponAsync(db, "TAKEN10", store.Id);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id);

        var request = new UpdateCouponRequest
        {
            Code = "taken10", // case-insensitive collision with the other coupon
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(10)
        };

        var act = () => sut.UpdateAsync(coupon.Id, request);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Update_KeepingSameCode_DoesNotThrowConflict()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id);

        var request = new UpdateCouponRequest
        {
            Code = "MINE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 25,
            ExpiryDate = DateTime.UtcNow.AddDays(10)
        };

        var result = await sut.UpdateAsync(coupon.Id, request);

        result.DiscountValue.Should().Be(25);
    }

    // ---------- Update: expiry validation ----------

    [Fact]
    public async Task Update_ExpiryInPast_ThrowsValidation()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id);

        var request = new UpdateCouponRequest
        {
            Code = "MINE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(-1)
        };

        var act = () => sut.UpdateAsync(coupon.Id, request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ---------- Update: UsageLimit >= UsageCount ----------

    [Fact]
    public async Task Update_UsageLimitBelowUsageCount_ThrowsValidation()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id, usageCount: 5, usageLimit: 10);

        var request = new UpdateCouponRequest
        {
            Code = "MINE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            UsageLimit = 3 // below the current UsageCount of 5
        };

        var act = () => sut.UpdateAsync(coupon.Id, request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Update_UsageLimitAtOrAboveUsageCount_Succeeds()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id, usageCount: 5, usageLimit: 10);

        var request = new UpdateCouponRequest
        {
            Code = "MINE10",
            DiscountType = CouponDiscountType.Percentage,
            DiscountValue = 20,
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            UsageLimit = 5
        };

        var result = await sut.UpdateAsync(coupon.Id, request);

        result.UsageLimit.Should().Be(5);
    }

    // ---------- Deactivate: ownership + effect ----------

    [Fact]
    public async Task Deactivate_SellerOwnStoresCoupon_SetsIsActiveFalse()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var store = await SeedStoreAsync(db, ownerUserId: 1);
        var coupon = await SeedCouponAsync(db, "MINE10", store.Id);

        await sut.DeactivateAsync(coupon.Id);

        var reloaded = await db.Coupons.FindAsync(coupon.Id);
        reloaded!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_SellerAnotherStoresCoupon_ThrowsForbidden()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var otherStore = await SeedStoreAsync(db, ownerUserId: 2);
        var coupon = await SeedCouponAsync(db, "OTHER10", otherStore.Id);

        var act = () => sut.DeactivateAsync(coupon.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ---------- GetAll: scoping ----------

    [Fact]
    public async Task GetAll_SellerNoFilter_OnlyReturnsOwnStoresCoupons()
    {
        var sut = BuildSut(out var db, out _, userId: 1, isAdmin: false);
        var myStore = await SeedStoreAsync(db, ownerUserId: 1);
        var otherStore = await SeedStoreAsync(db, ownerUserId: 2);
        await SeedCouponAsync(db, "MINE10", myStore.Id);
        await SeedCouponAsync(db, "OTHER10", otherStore.Id);
        await SeedCouponAsync(db, "GLOBAL10", storeId: null);

        var result = await sut.GetAllAsync(new CouponQueryParameters());

        result.Should().ContainSingle().Which.Code.Should().Be("MINE10");
    }

    [Fact]
    public async Task GetAll_AdminNoFilter_ReturnsEverything()
    {
        var sut = BuildSut(out var db, out _, userId: 99, isAdmin: true);
        var storeA = await SeedStoreAsync(db, ownerUserId: 1);
        var storeB = await SeedStoreAsync(db, ownerUserId: 2);
        await SeedCouponAsync(db, "A10", storeA.Id);
        await SeedCouponAsync(db, "B10", storeB.Id);
        await SeedCouponAsync(db, "GLOBAL10", storeId: null);

        var result = await sut.GetAllAsync(new CouponQueryParameters());

        result.Should().HaveCount(3);
    }
}

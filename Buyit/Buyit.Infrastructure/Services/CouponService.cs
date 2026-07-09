using Microsoft.EntityFrameworkCore;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Infrastructure.Services;

public class CouponService : ICouponService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<CreateCouponRequest> _createValidator;
    private readonly IValidator<UpdateCouponRequest> _updateValidator;

    public CouponService(
        AppDbContext db,
        ICurrentUserService currentUser,
        IValidator<CreateCouponRequest> createValidator,
        IValidator<UpdateCouponRequest> updateValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<List<CouponResponse>> GetAllAsync(CouponQueryParameters query)
    {
        IQueryable<Coupon> coupons = _db.Coupons.Include(c => c.Store);

        if (query.StoreId is not null)
        {
            await EnsureCanManageStoreAsync(query.StoreId.Value);
            coupons = coupons.Where(c => c.StoreId == query.StoreId.Value);
        }
        else if (!_currentUser.IsAdmin)
        {
            var userId = _currentUser.UserId
                ?? throw new ForbiddenException("You must be signed in to view coupons.");
            coupons = coupons.Where(c => c.StoreId != null && c.Store!.OwnerUserId == userId);
        }
        // Admin, no store filter: sees every coupon (global + all stores).

        var list = await coupons.OrderByDescending(c => c.Id).ToListAsync();
        return list.Select(ToResponse).ToList();
    }

    public async Task<CouponResponse> GetByIdAsync(int id)
    {
        var coupon = await _db.Coupons.Include(c => c.Store).FirstOrDefaultAsync(c => c.Id == id);
        if (coupon is null)
            throw new NotFoundException($"Coupon with id {id} was not found.");

        await EnsureCanManageCouponAsync(coupon);
        return ToResponse(coupon);
    }

    public async Task<CouponResponse> CreateAsync(CreateCouponRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        if (request.StoreId is null)
        {
            if (!_currentUser.IsAdmin)
                throw new ForbiddenException("Only an admin can create a platform-wide coupon.");
        }
        else
        {
            await EnsureCanManageStoreAsync(request.StoreId.Value);
        }

        var codeTaken = await _db.Coupons.AnyAsync(c => c.Code.ToLower() == request.Code.ToLower());
        if (codeTaken)
            throw new ConflictException($"A coupon with code '{request.Code}' already exists.");

        var coupon = new Coupon
        {
            Code = request.Code,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            ExpiryDate = request.ExpiryDate,
            UsageLimit = request.UsageLimit,
            UsageCount = 0,
            IsActive = true,
            StoreId = request.StoreId
        };
        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync();

        return await GetByIdAsync(coupon.Id);
    }

    public async Task<CouponResponse> UpdateAsync(int id, UpdateCouponRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        var coupon = await _db.Coupons.Include(c => c.Store).FirstOrDefaultAsync(c => c.Id == id);
        if (coupon is null)
            throw new NotFoundException($"Coupon with id {id} was not found.");

        await EnsureCanManageCouponAsync(coupon);

        if (!string.Equals(coupon.Code, request.Code, StringComparison.OrdinalIgnoreCase))
        {
            var codeTaken = await _db.Coupons
                .AnyAsync(c => c.Id != id && c.Code.ToLower() == request.Code.ToLower());
            if (codeTaken)
                throw new ConflictException($"A coupon with code '{request.Code}' already exists.");
        }

        if (request.UsageLimit is not null && request.UsageLimit < coupon.UsageCount)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["usageLimit"] = [$"UsageLimit ({request.UsageLimit}) cannot be lower than the current UsageCount ({coupon.UsageCount})."]
            });

        coupon.Code = request.Code;
        coupon.DiscountType = request.DiscountType;
        coupon.DiscountValue = request.DiscountValue;
        coupon.ExpiryDate = request.ExpiryDate;
        coupon.IsActive = request.IsActive;
        coupon.UsageLimit = request.UsageLimit;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(coupon.Id);
    }

    public async Task DeactivateAsync(int id)
    {
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == id);
        if (coupon is null)
            throw new NotFoundException($"Coupon with id {id} was not found.");

        await EnsureCanManageCouponAsync(coupon);

        coupon.IsActive = false;
        await _db.SaveChangesAsync();
    }

    // Admin bypasses. Otherwise: platform-wide coupons (StoreId == null) are admin-only;
    // store-scoped coupons require owning that store.
    private async Task EnsureCanManageCouponAsync(Coupon coupon)
    {
        if (_currentUser.IsAdmin) return;

        if (coupon.StoreId is null)
            throw new ForbiddenException("Only an admin can manage a platform-wide coupon.");

        await EnsureCanManageStoreAsync(coupon.StoreId.Value);
    }

    // Same ownership pattern as ProductService.EnsureCanManageStoreAsync.
    private async Task EnsureCanManageStoreAsync(int storeId)
    {
        if (_currentUser.IsAdmin) return;

        var userId = _currentUser.UserId;
        if (userId is null)
            throw new ForbiddenException("You are not allowed to manage this coupon.");

        var ownsStore = await _db.Stores.AnyAsync(s => s.Id == storeId && s.OwnerUserId == userId);
        if (!ownsStore)
            throw new ForbiddenException("You can only manage coupons in your own store.");
    }

    private static CouponResponse ToResponse(Coupon c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        DiscountType = c.DiscountType,
        DiscountValue = c.DiscountValue,
        ExpiryDate = c.ExpiryDate,
        IsActive = c.IsActive,
        UsageLimit = c.UsageLimit,
        UsageCount = c.UsageCount,
        StoreId = c.StoreId,
        StoreName = c.Store?.Name
    };
}

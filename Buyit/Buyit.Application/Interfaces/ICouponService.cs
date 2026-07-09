using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface ICouponService
{
    Task<List<CouponResponse>> GetAllAsync(CouponQueryParameters query);
    Task<CouponResponse> GetByIdAsync(int id);
    Task<CouponResponse> CreateAsync(CreateCouponRequest request);
    Task<CouponResponse> UpdateAsync(int id, UpdateCouponRequest request);
    Task DeactivateAsync(int id);
}
using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(int? sellerUserId);
    Task<IReadOnlyList<PeriodPointResponse>> GetRevenueByPeriodAsync(string period, int? sellerUserId); // "day"|"week"|"month"
    Task<IReadOnlyList<TopProductResponse>> GetTopProductsAsync(int? sellerUserId);
    Task<IReadOnlyList<PeriodPointResponse>> GetNewCustomersAsync(string period, int? sellerUserId);
    Task<IReadOnlyList<StatusCountResponse>> GetOrdersByStatusAsync(int? sellerUserId);

    // TB-66: assemble all the admin dashboard pieces into one response (admin scope only).
    Task<AdminDashboardResponse> GetAdminDashboardAsync(string period);
}
namespace Buyit.Application.DTOs;

public record DashboardSummaryResponse(
    decimal TotalRevenue,
    int TotalOrders,
    int TotalCustomers,
    int LowStockCount,
    int TodaysNewOrders,
    decimal? TotalCommission,   // admin-only; null for a seller view
    // Rolling 30-day hero metrics (vs. the prior 30-day window). Growth is null when the
    // prior window had nothing to compare against. AvgRating is null when there are no reviews.
    decimal Revenue30d,
    decimal? RevenueGrowthPct,
    int Orders30d,
    decimal? OrdersGrowthPct,
    decimal? AvgRating);
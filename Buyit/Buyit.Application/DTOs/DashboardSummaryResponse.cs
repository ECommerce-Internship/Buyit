namespace Buyit.Application.DTOs;

public record DashboardSummaryResponse(
    decimal TotalRevenue,
    int TotalOrders,
    int TotalCustomers,
    int LowStockCount,
    int TodaysNewOrders,
    decimal? TotalCommission);   // admin-only; null for a seller view
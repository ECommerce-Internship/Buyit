namespace Buyit.Application.DTOs;

// TB-66: one bundle of everything the Admin dashboard needs, so the frontend makes a SINGLE call
// instead of five. Each field is just an existing dashboard response type, reused as-is.
public record AdminDashboardResponse(
    DashboardSummaryResponse Summary,
    IReadOnlyList<PeriodPointResponse> Revenue,
    IReadOnlyList<StatusCountResponse> OrdersByStatus,
    IReadOnlyList<TopProductResponse> TopProducts,
    IReadOnlyList<PeriodPointResponse> NewCustomers);

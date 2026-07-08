using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Enums;
using Buyit.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Buyit.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    private readonly ICacheService _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    public DashboardService(AppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string Scope(int? sellerUserId) => sellerUserId is null ? "admin" : $"seller:{sellerUserId}";

    // Base query of StoreOrders, optionally scoped to a seller's stores.
    private IQueryable<Domain.Entities.StoreOrder> StoreOrders(int? sellerUserId)
    {
        var q = _db.StoreOrders.AsQueryable();
        if (sellerUserId is not null)
            q = q.Where(so => so.Store.OwnerUserId == sellerUserId);
        return q;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(int? sellerUserId)
    {
        var key = $"dashboard:summary:{Scope(sellerUserId)}";
        var cached = await _cache.GetAsync<DashboardSummaryResponse>(key);
        if (cached is not null) return cached;

        var today = DateTime.UtcNow.Date;
        DashboardSummaryResponse result;

        if (sellerUserId is null)
        {
            // ADMIN: platform-wide.
            var totalRevenue = await _db.Payments
                .Where(p => p.Status == PaymentStatus.Paid)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var totalOrders = await _db.Orders.CountAsync();
            var totalCustomers = await _db.Users.CountAsync(u => u.Role == UserRole.Customer);
            var lowStock = await _db.Inventories
                .CountAsync(i => !i.Product.IsDeleted && i.QuantityInStock <= i.LowStockThreshold);
            var todays = await _db.Orders.CountAsync(o => o.OrderDate >= today);
            var commission = await _db.StoreOrders.SumAsync(so => (decimal?)so.CommissionAmount) ?? 0m;

            result = new DashboardSummaryResponse(totalRevenue, totalOrders, totalCustomers, lowStock, todays, commission);
        }
        else
        {
            // SELLER: scoped to their stores.
            var so = StoreOrders(sellerUserId);
            var revenue = await so.SumAsync(x => (decimal?)x.SubTotal) ?? 0m;
            var orders = await so.Select(x => x.OrderId).Distinct().CountAsync();
            var lowStock = await _db.Inventories
                .CountAsync(i => !i.Product.IsDeleted
                                 && i.Product.Store.OwnerUserId == sellerUserId
                                 && i.QuantityInStock <= i.LowStockThreshold);
            var todays = await so.Where(x => x.Order.OrderDate >= today).Select(x => x.OrderId).Distinct().CountAsync();
            // TotalCustomers/commission aren't meaningful for a single seller -> 0 / null.
            result = new DashboardSummaryResponse(revenue, orders, 0, lowStock, todays, null);
        }

        await _cache.SetAsync(key, result, Ttl);
        return result;
    }

    public async Task<IReadOnlyList<TopProductResponse>> GetTopProductsAsync(int? sellerUserId)
    {
        var key = $"dashboard:top:{Scope(sellerUserId)}";
        var cached = await _cache.GetAsync<List<TopProductResponse>>(key);
        if (cached is not null) return cached;

        var items = _db.StoreOrderItems.AsQueryable();
        if (sellerUserId is not null)
            items = items.Where(i => i.StoreOrder.Store.OwnerUserId == sellerUserId);

        // Group + aggregate in SQL into an ANONYMOUS type (EF can't translate a record
        // constructor inside a GroupBy projection), then map to the DTO in memory.
        var grouped = await items
            .GroupBy(i => new { i.ProductId, i.ProductNameSnapshot })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductNameSnapshot,
                UnitsSold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.Subtotal)
            })
            .OrderByDescending(t => t.UnitsSold)
            .Take(10)
            .ToListAsync();

        var top = grouped
            .Select(g => new TopProductResponse(g.ProductId, g.ProductNameSnapshot, g.UnitsSold, g.Revenue))
            .ToList();

        await _cache.SetAsync(key, top, Ttl);
        return top;
    }

    public async Task<IReadOnlyList<StatusCountResponse>> GetOrdersByStatusAsync(int? sellerUserId)
    {
        var key = $"dashboard:status:{Scope(sellerUserId)}";
        var cached = await _cache.GetAsync<List<StatusCountResponse>>(key);
        if (cached is not null) return cached;

        var rows = await StoreOrders(sellerUserId)
            .GroupBy(so => so.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        var result = rows.Select(r => new StatusCountResponse(r.Key.ToString(), r.Count)).ToList();
        await _cache.SetAsync(key, result, Ttl);
        return result;
    }

    public async Task<IReadOnlyList<PeriodPointResponse>> GetRevenueByPeriodAsync(string period, int? sellerUserId)
    {
        var key = $"dashboard:revenue:{period}:{Scope(sellerUserId)}";
        var cached = await _cache.GetAsync<List<PeriodPointResponse>>(key);
        if (cached is not null) return cached;

        var (cutoff, granularity) = ResolveRange(period);

        // Pull minimal rows from the DB (scoped to the range's cutoff), then bucket in memory.
        List<(DateTime When, decimal Amount)> raw;
        if (sellerUserId is null)
        {
            var paymentsQuery = _db.Payments.Where(p => p.Status == PaymentStatus.Paid && p.PaidAt != null);
            if (cutoff is not null) paymentsQuery = paymentsQuery.Where(p => p.PaidAt >= cutoff);
            raw = (await paymentsQuery
                .Select(p => new { p.PaidAt, p.Amount })
                .ToListAsync())
                .Select(x => (x.PaidAt!.Value, x.Amount)).ToList();
        }
        else
        {
            var ordersQuery = StoreOrders(sellerUserId);
            if (cutoff is not null) ordersQuery = ordersQuery.Where(so => so.Order.OrderDate >= cutoff);
            raw = (await ordersQuery
                .Select(so => new { so.Order.OrderDate, so.SubTotal })
                .ToListAsync())
                .Select(x => (x.OrderDate, x.SubTotal)).ToList();
        }

        var result = raw
            .GroupBy(r => BucketLabel(r.When, granularity))
            .Select(g => new PeriodPointResponse(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(p => p.Period)
            .ToList();

        await _cache.SetAsync(key, result, Ttl);
        return result;
    }

    public async Task<IReadOnlyList<PeriodPointResponse>> GetNewCustomersAsync(string period, int? sellerUserId)
    {
        // New customers is a platform metric; for a seller scope it's not meaningful -> empty.
        if (sellerUserId is not null) return new List<PeriodPointResponse>();

        var key = $"dashboard:newcustomers:{period}:admin";
        var cached = await _cache.GetAsync<List<PeriodPointResponse>>(key);
        if (cached is not null) return cached;

        var (cutoff, granularity) = ResolveRange(period);
        var usersQuery = _db.Users.Where(u => u.Role == UserRole.Customer);
        if (cutoff is not null) usersQuery = usersQuery.Where(u => u.CreatedAt >= cutoff);

        var dates = await usersQuery.Select(u => u.CreatedAt).ToListAsync();

        var result = dates
            .GroupBy(d => BucketLabel(d, granularity))
            .Select(g => new PeriodPointResponse(g.Key, g.Count()))
            .OrderBy(p => p.Period)
            .ToList();

        await _cache.SetAsync(key, result, Ttl);
        return result;
    }

    public async Task<AdminDashboardResponse> GetAdminDashboardAsync(string period)
    {
        // Admin scope = pass null for sellerUserId. Each call is independently cached (2-min TTL).
        var summary = await GetSummaryAsync(null);
        var revenue = await GetRevenueByPeriodAsync(period, null);
        var byStatus = await GetOrdersByStatusAsync(null);
        var topProducts = await GetTopProductsAsync(null);
        var newCustomers = await GetNewCustomersAsync(period, null);

        return new AdminDashboardResponse(summary, revenue, byStatus, topProducts, newCustomers);
    }

    // Turns a timestamp into a bucket label for the requested period.
    // Maps a frontend range token to a lookback cutoff + bucket granularity. Null cutoff means
    // "no window filter" (all-time), used by the legacy "month" default so nothing else breaks.
    private static (DateTime? CutoffUtc, string Granularity) ResolveRange(string range) => range?.ToLowerInvariant() switch
    {
        "1d" => (DateTime.UtcNow.AddDays(-1), "hour"),
        "15d" => (DateTime.UtcNow.AddDays(-15), "day"),
        "30d" => (DateTime.UtcNow.AddDays(-30), "day"),
        "3m" => (DateTime.UtcNow.AddMonths(-3), "week"),
        "6m" => (DateTime.UtcNow.AddMonths(-6), "month"),
        "1y" => (DateTime.UtcNow.AddYears(-1), "month"),
        "day" => (null, "day"),
        "week" => (null, "week"),
        _ => (null, "month"),   // "month" (default) — unchanged, all-time monthly buckets
    };

    // Turns a timestamp into a bucket label for the resolved granularity.
    private static string BucketLabel(DateTime dt, string granularity) => granularity switch
    {
        "hour" => dt.ToString("yyyy-MM-dd HH:00"),
        "day" => dt.ToString("yyyy-MM-dd"),
        "week" => $"{System.Globalization.ISOWeek.GetYear(dt)}-W{System.Globalization.ISOWeek.GetWeekOfYear(dt):D2}",
        _ => dt.ToString("yyyy-MM")   // "month"
    };
}
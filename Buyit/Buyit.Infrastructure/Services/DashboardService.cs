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

        var now = DateTime.UtcNow;
        var today = now.Date;
        var d30 = now.AddDays(-30);   // start of the current rolling 30-day window
        var d60 = now.AddDays(-60);   // start of the prior 30-day window
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

            // Rolling 30-day revenue (paid payments) vs. the prior 30 days.
            var revenue30d = await _db.Payments
                .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= d30)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var revenuePrev = await _db.Payments
                .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= d60 && p.PaidAt < d30)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var orders30d = await _db.Orders.CountAsync(o => o.OrderDate >= d30);
            var ordersPrev = await _db.Orders.CountAsync(o => o.OrderDate >= d60 && o.OrderDate < d30);

            var avgRating = await AverageRatingAsync(sellerUserId);

            result = new DashboardSummaryResponse(
                totalRevenue, totalOrders, totalCustomers, lowStock, todays, commission,
                revenue30d, GrowthPct(revenue30d, revenuePrev),
                orders30d, GrowthPct(orders30d, ordersPrev),
                avgRating);
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

            // Rolling 30-day revenue (own subtotals) vs. the prior 30 days.
            var revenue30d = await so.Where(x => x.Order.OrderDate >= d30).SumAsync(x => (decimal?)x.SubTotal) ?? 0m;
            var revenuePrev = await so.Where(x => x.Order.OrderDate >= d60 && x.Order.OrderDate < d30)
                .SumAsync(x => (decimal?)x.SubTotal) ?? 0m;

            var orders30d = await so.Where(x => x.Order.OrderDate >= d30).Select(x => x.OrderId).Distinct().CountAsync();
            var ordersPrev = await so.Where(x => x.Order.OrderDate >= d60 && x.Order.OrderDate < d30)
                .Select(x => x.OrderId).Distinct().CountAsync();

            var avgRating = await AverageRatingAsync(sellerUserId);

            // TotalCustomers/commission aren't meaningful for a single seller -> 0 / null.
            result = new DashboardSummaryResponse(
                revenue, orders, 0, lowStock, todays, null,
                revenue30d, GrowthPct(revenue30d, revenuePrev),
                orders30d, GrowthPct(orders30d, ordersPrev),
                avgRating);
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

        // A range token ("1d".."1y") resolves to a rolling window + a sensible bucket size;
        // legacy tokens ("day"/"week"/"month") keep the old full-history behaviour (Start == null).
        var now = DateTime.UtcNow;
        var (start, bucket, step) = ResolveRange(period, now);

        // Pull minimal rows from the DB (filtered to the window when there is one), then bucket in memory.
        List<(DateTime When, decimal Amount)> raw;
        if (sellerUserId is null)
        {
            var q = _db.Payments.Where(p => p.Status == PaymentStatus.Paid && p.PaidAt != null);
            if (start is not null) q = q.Where(p => p.PaidAt >= start);
            raw = (await q.Select(p => new { p.PaidAt, p.Amount }).ToListAsync())
                .Select(x => (x.PaidAt!.Value, x.Amount)).ToList();
        }
        else
        {
            var q = StoreOrders(sellerUserId);
            if (start is not null) q = q.Where(so => so.Order.OrderDate >= start);
            raw = (await q.Select(so => new { so.Order.OrderDate, so.SubTotal }).ToListAsync())
                .Select(x => (x.OrderDate, x.SubTotal)).ToList();
        }

        var sums = raw
            .GroupBy(r => BucketLabel(r.When, bucket))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        List<PeriodPointResponse> result;
        if (start is not null)
        {
            // Zero-fill every bucket across the window so the series is a continuous line
            // (a single payment shows a trend, not one lonely dot). Walk the window at `step`
            // resolution and keep each distinct bucket label in chronological order.
            var labels = new List<string>();
            var seen = new HashSet<string>();
            for (var t = start.Value; t <= now; t = t.Add(step))
            {
                var lbl = BucketLabel(t, bucket);
                if (seen.Add(lbl)) labels.Add(lbl);
            }
            result = labels
                .Select(l => new PeriodPointResponse(l, sums.TryGetValue(l, out var v) ? v : 0m))
                .ToList();
        }
        else
        {
            result = sums
                .Select(kv => new PeriodPointResponse(kv.Key, kv.Value))
                .OrderBy(p => p.Period)
                .ToList();
        }

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

        var now = DateTime.UtcNow;
        var (start, bucket, _) = ResolveRange(period, now);
        var usersQuery = _db.Users.Where(u => u.Role == UserRole.Customer);
        if (start is not null) usersQuery = usersQuery.Where(u => u.CreatedAt >= start);

        var dates = await usersQuery.Select(u => u.CreatedAt).ToListAsync();

        var result = dates
            .GroupBy(d => BucketLabel(d, bucket))
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

    // Average product rating, rounded to 1 dp. Null when there are no reviews (empty set).
    // Admin scope = all reviews; seller scope = reviews on the seller's own products only.
    private async Task<decimal?> AverageRatingAsync(int? sellerUserId)
    {
        var reviews = _db.Reviews.AsQueryable();
        if (sellerUserId is not null)
            reviews = reviews.Where(r => r.Product.Store.OwnerUserId == sellerUserId);

        // (double?) makes AVG over an empty set return null instead of throwing.
        var avg = await reviews.AverageAsync(r => (double?)r.Rating);
        return avg is null ? null : Math.Round((decimal)avg.Value, 1);
    }

    // Percentage change from a prior value to a current one, rounded to 1 dp.
    // Null when the prior window is zero (no meaningful baseline to grow from).
    private static decimal? GrowthPct(decimal current, decimal prior)
        => prior == 0m ? null : Math.Round((current - prior) / prior * 100m, 1);

    // Maps a range token to a rolling-window start, the bucket granularity to group by, and the
    // step to walk the window when zero-filling. Legacy tokens ("day"/"week"/"month") return a
    // null start -> no window, full history, matching the original behaviour.
    private static (DateTime? Start, string Bucket, TimeSpan Step) ResolveRange(string? token, DateTime now) =>
        token?.ToLowerInvariant() switch
        {
            "1d" => (now.AddDays(-1), "hour", TimeSpan.FromHours(1)),
            "15d" => (now.AddDays(-15), "day", TimeSpan.FromDays(1)),
            "30d" => (now.AddDays(-30), "day", TimeSpan.FromDays(1)),
            "3m" => (now.AddMonths(-3), "week", TimeSpan.FromDays(1)),
            "6m" => (now.AddMonths(-6), "month", TimeSpan.FromDays(1)),
            "1y" => (now.AddYears(-1), "month", TimeSpan.FromDays(1)),
            _ => (null, token ?? "month", TimeSpan.FromDays(1)),   // legacy: no window
        };

    // Turns a timestamp into a bucket label for the requested granularity.
    private static string BucketLabel(DateTime dt, string period) => period?.ToLowerInvariant() switch
    {
        "hour" => dt.ToString("yyyy-MM-dd HH:00"),
        "day" => dt.ToString("yyyy-MM-dd"),
        "week" => $"{System.Globalization.ISOWeek.GetYear(dt)}-W{System.Globalization.ISOWeek.GetWeekOfYear(dt):D2}",
        _ => dt.ToString("yyyy-MM")   // "month"
    };
}
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buyit.Infrastructure.Services;

/// <summary>
/// Dev-only demo-data generator. Creates buyer accounts, spreads their purchases evenly
/// across every approved store (guaranteeing each store has sales), and adds paid payments
/// plus reviews on purchased products — purely so the dashboards have something to show.
/// Additive only: it never touches existing products, stores or inventory.
/// </summary>
public class DataSeedService : IDataSeedService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataSeedService> _logger;

    public DataSeedService(AppDbContext db, ILogger<DataSeedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Lightweight views of the existing catalogue so we don't drag tracked entities around.
    private sealed record SeedProduct(int Id, string Name, decimal Price);
    private sealed record SeedStore(int Id, decimal CommissionRate, List<SeedProduct> Products);

    private static readonly string[] FirstNames =
        { "Ava", "Liam", "Noah", "Mia", "Omar", "Sara", "Yuki", "Leo", "Nina", "Karim",
          "Elena", "Hassan", "Zoe", "Diego", "Aisha", "Tom", "Lina", "Marco", "Priya", "Jonas" };
    private static readonly string[] LastNames =
        { "Khan", "Silva", "Chen", "Novak", "Haddad", "Rossi", "Meyer", "Kaur", "Okafor", "Ito",
          "Nguyen", "Cohen", "Popov", "Diallo", "Garcia", "Ali", "Berg", "Costa", "Sharma", "Park" };
    private static readonly (string City, string State, string Postal, string Country)[] Addresses =
        { ("Berlin", "Berlin", "10115", "Germany"), ("Cairo", "Cairo", "11511", "Egypt"),
          ("Lisbon", "Lisboa", "1100", "Portugal"), ("Austin", "TX", "73301", "USA"),
          ("Toronto", "ON", "M5H", "Canada"), ("Dubai", "Dubai", "00000", "UAE") };
    private static readonly string[] Comments =
        { "Exactly as described, fast shipping.", "Good value for the price.", "Works great, would buy again.",
          "Decent quality, packaging could be better.", "Really happy with this purchase.",
          "Solid product, does the job.", "Better than I expected.", "Arrived on time and well packed." };

    public async Task<SeedDataResponse> SeedDemoDataAsync(SeedDataRequest request)
    {
        request ??= new SeedDataRequest();

        // Clamp inputs to sane bounds so a stray value can't create runaway data.
        var customerCount = Math.Clamp(request.Customers, 1, 500);
        var orderTarget = Math.Clamp(request.Orders, 1, 5000);
        var daysBack = Math.Clamp(request.DaysBack, 1, 1095);
        var rng = request.Seed is int s ? new Random(s) : new Random();

        var now = DateTime.UtcNow;

        // --- Load the existing catalogue: approved stores that actually have products. ---
        // Product query filter (!IsDeleted) is applied to the projected navigation by EF.
        var stores = (await _db.Stores
                .Where(st => st.Status == StoreStatus.Approved)
                .Select(st => new
                {
                    st.Id,
                    st.CommissionRate,
                    Products = st.Products.Select(p => new SeedProduct(p.Id, p.Name, p.Price)).ToList()
                })
                .ToListAsync())
            .Where(st => st.Products.Count > 0)
            .Select(st => new SeedStore(st.Id, st.CommissionRate, st.Products))
            .ToList();

        if (stores.Count == 0)
            throw new ConflictException("No approved stores with products exist to seed sales against.");

        // --- Create buyer accounts, each signing up at a random point in the window. ---
        var customers = new List<User>(customerCount);
        for (var i = 0; i < customerCount; i++)
        {
            var first = FirstNames[rng.Next(FirstNames.Length)];
            var last = LastNames[rng.Next(LastNames.Length)];
            customers.Add(new User
            {
                FirstName = first,
                LastName = last,
                Email = $"seed.{first}.{last}.{Guid.NewGuid():N}".ToLowerInvariant()[..40] + "@buyit.test",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seed123!"),
                Role = UserRole.Customer,
                CreatedAt = now.AddDays(-rng.Next(0, daysBack)).AddHours(-rng.Next(0, 24))
            });
        }
        _db.Users.AddRange(customers);
        await _db.SaveChangesAsync();   // assign customer Ids so orders/reviews can reference them

        // Guarantee every store gets a handful of sales, then fill the rest at random.
        const int minOrdersPerStore = 3;
        var coverageOrders = stores.Count * minOrdersPerStore;
        var totalOrders = Math.Max(orderTarget, coverageOrders);

        var orders = new List<Order>(totalOrders);
        var reviews = new List<Review>();
        var reviewedPairs = new HashSet<(int UserId, int ProductId)>();   // enforce the unique (UserId, ProductId) index
        var buyerPtr = 0;   // round-robin so purchases spread evenly across buyers
        int storeOrderCount = 0, lineItemCount = 0;

        void CreateOrder(SeedStore? primaryStore)
        {
            var buyer = customers[buyerPtr++ % customers.Count];

            // Order date sits between the buyer's signup and now (never before they existed).
            var span = (now - buyer.CreatedAt).TotalDays;
            var orderDate = buyer.CreatedAt.AddDays(rng.NextDouble() * Math.Max(span, 0));

            // Build the cart: the primary store (for coverage) plus 0–2 extra random stores.
            var picked = new List<SeedStore>();
            if (primaryStore is not null) picked.Add(primaryStore);
            var extra = rng.Next(0, 3);
            for (var e = 0; e < extra && picked.Count < stores.Count; e++)
            {
                var candidate = stores[rng.Next(stores.Count)];
                if (!picked.Contains(candidate)) picked.Add(candidate);
            }
            if (picked.Count == 0) picked.Add(stores[rng.Next(stores.Count)]);

            var order = new Order
            {
                UserId = buyer.Id,
                OrderDate = orderDate,
                ShippingLine1 = $"{rng.Next(1, 200)} Market St",
                ShippingCity = "PLACEHOLDER",
                ShippingPostalCode = "00000",
                ShippingCountry = "PLACEHOLDER",
                ShippingState = "PLACEHOLDER"
            };
            var addr = Addresses[rng.Next(Addresses.Length)];
            order.ShippingCity = addr.City;
            order.ShippingState = addr.State;
            order.ShippingPostalCode = addr.Postal;
            order.ShippingCountry = addr.Country;

            decimal orderTotal = 0m;

            foreach (var store in picked)
            {
                // 1–3 distinct products from this store, varied quantities.
                var lineCount = Math.Min(rng.Next(1, 4), store.Products.Count);
                var chosen = PickDistinct(store.Products, lineCount, rng);

                var storeOrder = new StoreOrder
                {
                    StoreId = store.Id,
                    Status = RandomStatus(rng)
                };

                decimal subTotal = 0m;
                foreach (var product in chosen)
                {
                    var qty = rng.Next(1, 6);          // 1..5
                    var lineSubtotal = product.Price * qty;
                    subTotal += lineSubtotal;
                    storeOrder.StoreOrderItems.Add(new StoreOrderItem
                    {
                        ProductId = product.Id,
                        Quantity = qty,
                        UnitPrice = product.Price,
                        ProductNameSnapshot = product.Name,
                        Subtotal = lineSubtotal
                    });
                    lineItemCount++;

                    // A delivered line can earn a review from the buyer (once per product).
                    if (storeOrder.Status == OrderStatus.Delivered
                        && rng.NextDouble() < 0.6
                        && reviewedPairs.Add((buyer.Id, product.Id)))
                    {
                        var reviewDate = orderDate.AddDays(rng.Next(1, 10));
                        if (reviewDate > now) reviewDate = now;
                        reviews.Add(new Review
                        {
                            UserId = buyer.Id,
                            ProductId = product.Id,
                            Rating = WeightedRating(rng),
                            Comment = Comments[rng.Next(Comments.Length)],
                            CreatedAt = reviewDate
                        });
                    }
                }

                storeOrder.SubTotal = subTotal;
                storeOrder.CommissionAmount = Math.Round(subTotal * store.CommissionRate, 2, MidpointRounding.AwayFromZero);
                storeOrder.SellerNetAmount = subTotal - storeOrder.CommissionAmount;
                order.StoreOrders.Add(storeOrder);
                orderTotal += subTotal;
                storeOrderCount++;
            }

            order.TotalAmount = orderTotal;

            // One paid payment per order (admin revenue reads Payments by PaidAt).
            var paidAt = orderDate.AddMinutes(rng.Next(1, 90));
            if (paidAt > now) paidAt = now;
            order.Payment = new Payment
            {
                Amount = orderTotal,
                Method = (PaymentMethod)rng.Next(0, 3),
                Status = PaymentStatus.Paid,
                PaidAt = paidAt,
                TransactionId = $"SEED-{Guid.NewGuid():N}"[..20]
            };

            orders.Add(order);
        }

        // Coverage pass: each store is the primary of a few orders → guaranteed sales everywhere.
        foreach (var store in stores)
            for (var i = 0; i < minOrdersPerStore; i++)
                CreateOrder(store);

        // Random fill pass up to the requested total.
        for (var i = coverageOrders; i < totalOrders; i++)
            CreateOrder(primaryStore: null);

        _db.Orders.AddRange(orders);
        _db.Reviews.AddRange(reviews);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Demo-data seed created {Customers} customers, {Orders} orders, {StoreOrders} store-orders, {Lines} line items, {Reviews} reviews across {Stores} stores.",
            customers.Count, orders.Count, storeOrderCount, lineItemCount, reviews.Count, stores.Count);

        return new SeedDataResponse(
            customers.Count, orders.Count, storeOrderCount, lineItemCount, orders.Count, reviews.Count);
    }

    // Delivered-heavy status mix so most orders count as completed sales.
    private static OrderStatus RandomStatus(Random rng)
    {
        var roll = rng.Next(0, 100);
        return roll switch
        {
            < 60 => OrderStatus.Delivered,
            < 75 => OrderStatus.Shipped,
            < 88 => OrderStatus.Confirmed,
            < 96 => OrderStatus.Pending,
            _ => OrderStatus.Cancelled
        };
    }

    // Ratings skew positive, as real review distributions tend to.
    private static int WeightedRating(Random rng)
    {
        var roll = rng.Next(0, 100);
        return roll switch
        {
            < 55 => 5,
            < 80 => 4,
            < 92 => 3,
            < 98 => 2,
            _ => 1
        };
    }

    private static List<SeedProduct> PickDistinct(List<SeedProduct> source, int count, Random rng)
    {
        if (count >= source.Count) return new List<SeedProduct>(source);
        var pool = new List<SeedProduct>(source);
        var result = new List<SeedProduct>(count);
        for (var i = 0; i < count; i++)
        {
            var idx = rng.Next(pool.Count);
            result.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return result;
    }
}

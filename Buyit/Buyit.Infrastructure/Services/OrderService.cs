using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Buyit.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IValidator<PlaceOrderRequest> _placeOrderValidator;
    private readonly ICacheService _cache;
    private readonly IValidator<UpdateOrderStatusRequest> _updateStatusValidator;

    public OrderService(
        AppDbContext context,
        IEmailService emailService,
        IValidator<PlaceOrderRequest> placeOrderValidator,
        ICacheService cache,
        IValidator<UpdateOrderStatusRequest> updateStatusValidator)
    {
        _context = context;
        _emailService = emailService;
        _placeOrderValidator = placeOrderValidator;
        _cache = cache;
        _updateStatusValidator = updateStatusValidator;
    }

    // Allowed status transitions, enforced per StoreOrder.
    private static readonly Dictionary<OrderStatus, List<OrderStatus>> ValidProgressions = new()
    {
        [OrderStatus.Pending]   = new() { OrderStatus.Confirmed, OrderStatus.Cancelled },
        [OrderStatus.Confirmed] = new() { OrderStatus.Shipped, OrderStatus.Cancelled },
        [OrderStatus.Shipped]   = new() { OrderStatus.Delivered },
        [OrderStatus.Delivered] = new(),
        [OrderStatus.Cancelled] = new()
    };

    public async Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request)
    {
        var validationResult = await _placeOrderValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new Buyit.Domain.Exceptions.ValidationException(errors);
        }

        // (1) Load the cart with items -> product -> inventory -> store, plus the coupon.
        var cart = await _context.Carts
            .Include(c => c.CartItems).ThenInclude(ci => ci.Product).ThenInclude(p => p.Inventory)
            .Include(c => c.CartItems).ThenInclude(ci => ci.Product).ThenInclude(p => p.Store)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart is null || !cart.CartItems.Any())
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["cart"] = ["Cart is empty. Add items before placing an order."]
            });

        // (2) Up-front stock check across ALL items.
        var stockErrors = new List<string>();
        foreach (var item in cart.CartItems)
        {
            var available = item.Product.Inventory?.QuantityInStock ?? 0;
            if (available < item.Quantity)
                stockErrors.Add($"Insufficient stock for '{item.Product.Name}'. Available: {available}, Requested: {item.Quantity}.");
        }
        if (stockErrors.Any())
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["stock"] = stockErrors.ToArray()
            });

        // (3) Re-validate the coupon at order time.
        var coupon = cart.Coupon;
        if (coupon != null && (!coupon.IsActive || coupon.ExpiryDate < DateTime.UtcNow))
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["coupon"] = [$"Coupon '{coupon.Code}' is no longer valid. Please remove it and try again."]
            });

        // Store-scoped coupons only apply if the cart still contains something from that store
        // (items may have been added/removed since the coupon was applied to the cart).
        if (coupon != null && coupon.StoreId is not null
            && !cart.CartItems.Any(ci => ci.Product.StoreId == coupon.StoreId))
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["coupon"] = [$"Coupon '{coupon.Code}' no longer applies — your cart has no items from that store."]
            });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // (4) Parent order.
            var order = new Order
            {
                UserId = userId,
                OrderDate = DateTime.UtcNow,
                ShippingLine1 = request.ShippingLine1,
                ShippingLine2 = request.ShippingLine2,
                ShippingCity = request.ShippingCity,
                ShippingState = request.ShippingState,
                ShippingPostalCode = request.ShippingPostalCode,
                ShippingCountry = request.ShippingCountry,
                CouponId = coupon?.Id
            };

            // (5) FAN OUT: one StoreOrder per distinct store in the cart.
            decimal subtotalAll = 0m;
            // Captures the subtotal of just the coupon's own store, when the coupon is store-scoped —
            // a store-scoped coupon must only discount that store's slice, never the whole order.
            decimal? couponStoreSubtotal = null;
            foreach (var group in cart.CartItems.GroupBy(ci => ci.Product.StoreId))
            {
                var store = group.First().Product.Store;
                var storeOrder = new StoreOrder
                {
                    StoreId = group.Key,
                    Status = OrderStatus.Pending
                };
                decimal storeSubtotal = 0m;
                foreach (var item in group)
                {
                    var line = item.Product.Price * item.Quantity;
                    storeSubtotal += line;
                    storeOrder.StoreOrderItems.Add(new StoreOrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.Product.Price,
                        ProductNameSnapshot = item.Product.Name,
                        Subtotal = line
                    });
                    item.Product.Inventory!.QuantityInStock -= item.Quantity;
                    item.Product.Inventory.LastUpdated = DateTime.UtcNow;
                }
                storeOrder.SubTotal = storeSubtotal;
                storeOrder.CommissionAmount = Math.Round(storeSubtotal * store.CommissionRate, 2);
                storeOrder.SellerNetAmount = storeSubtotal - storeOrder.CommissionAmount;
                subtotalAll += storeSubtotal;
                order.StoreOrders.Add(storeOrder);

                if (coupon is not null && coupon.StoreId == group.Key)
                    couponStoreSubtotal = storeSubtotal;
            }

            // (6) Order-level discount + total.
            // Re-check the usage limit here (not just in CartService.ApplyCouponAsync) — time may have
            // passed between adding the coupon to the cart and actually checking out, so another
            // customer could have used up the last slot in between.
            if (coupon is not null && coupon.UsageLimit is not null && coupon.UsageCount >= coupon.UsageLimit)
                throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
                {
                    ["coupon"] = ["This coupon has reached its usage limit."]
                });

            decimal discountAmount = 0m;
            if (coupon is not null)
            {
                // Store-scoped coupons only discount THEIR store's slice of the order, never the
                // whole multi-vendor cart — a global coupon (StoreId == null) still discounts everything.
                var discountBase = coupon.StoreId is null ? subtotalAll : (couponStoreSubtotal ?? 0m);

                if (coupon.DiscountType == Buyit.Domain.Enums.CouponDiscountType.Percentage)
                {
                    discountAmount = Math.Round(discountBase * (coupon.DiscountValue / 100m), 2);
                }
                else
                {
                    // FixedAmount: a flat amount off, never discounting past zero.
                    discountAmount = Math.Min(coupon.DiscountValue, discountBase);
                }

                // Record the redemption atomically with the order (same SaveChangesAsync call below).
                coupon.UsageCount++;
            }
            order.DiscountAmount = discountAmount;
            order.TotalAmount = subtotalAll - order.DiscountAmount;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // (7) Clear the cart + remove coupon.
            var affectedProductIds = cart.CartItems.Select(ci => ci.ProductId).Distinct().ToList();
            _context.CartItems.RemoveRange(cart.CartItems);
            cart.CouponId = null;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            foreach (var productId in affectedProductIds)
                await _cache.InvalidateProductAsync(productId);

            var userEmail = (await _context.Users.FindAsync(userId))?.Email ?? string.Empty;
            _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, userEmail, order.TotalAmount));

            return await BuildOrderResponseAsync(order.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            // H1: another checkout changed an item's stock between our read and our write.
            await transaction.RollbackAsync();
            throw new ConflictException(
                "The stock for one or more items changed while placing your order. Please try again.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<PaginatedResult<OrderSummaryResponse>> GetMyOrdersAsync(int userId, int page, int pageSize)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);   // M2: clamp page size

        var query = _context.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.OrderDate);

        var totalCount = await query.CountAsync();

        // Pull the raw fields (+ each store-slice status) then roll up the status in memory.
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                CustomerEmail = o.User.Email,
                o.OrderDate,
                o.TotalAmount,
                StoreOrderCount = o.StoreOrders.Count,
                ItemCount = o.StoreOrders.Sum(so => so.StoreOrderItems.Count),
                Statuses = o.StoreOrders.Select(so => so.Status).ToList(),
                PaymentStatus = o.Payment != null ? o.Payment.Status.ToString() : null
            })
            .ToListAsync();

        var items = rows.Select(r => new OrderSummaryResponse(
            r.Id, r.CustomerEmail, r.OrderDate, RollUpStatus(r.Statuses), r.TotalAmount,
            r.StoreOrderCount, r.ItemCount, r.PaymentStatus)).ToList();

        return new PaginatedResult<OrderSummaryResponse>
        {
            Items = items, Page = page, PageSize = pageSize,
            TotalCount = totalCount, TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<OrderResponse> GetOrderByIdAsync(int orderId, int userId, bool isAdmin)
    {
        var order = await LoadOrderGraphAsync(orderId);
        if (order is null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (!isAdmin && order.UserId != userId)
            throw new ForbiddenException("You do not have permission to view this order.");

        return MapToOrderResponse(order);
    }

    public async Task CancelStoreOrderAsync(int storeOrderId, int callerUserId, bool isAdmin)
    {
        var storeOrder = await _context.StoreOrders
            .Include(so => so.Order)
            .Include(so => so.Store)
            .FirstOrDefaultAsync(so => so.Id == storeOrderId);
        if (storeOrder is null)
            throw new NotFoundException($"StoreOrder with ID {storeOrderId} was not found.");

        var isBuyer = storeOrder.Order.UserId == callerUserId;
        var isSeller = storeOrder.Store.OwnerUserId == callerUserId;
        if (!isAdmin && !isBuyer && !isSeller)
            throw new ForbiddenException("You do not have permission to cancel this store order.");

        if (storeOrder.Status != OrderStatus.Pending)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Only Pending store orders can be cancelled (current: {storeOrder.Status})."]
            });

        await RestockStoreOrderAsync(storeOrderId);
        storeOrder.Status = OrderStatus.Cancelled;
        await _context.SaveChangesAsync();
    }

    public async Task<PaginatedResult<OrderSummaryResponse>> GetAllOrdersAsync(
        int page, int pageSize, string? status, DateTime? from, DateTime? to)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);   // M2: clamp page size

        var query = _context.Orders.AsQueryable();

        if (from.HasValue) query = query.Where(o => o.OrderDate >= from.Value);
        if (to.HasValue) query = query.Where(o => o.OrderDate <= to.Value);

        // M1: filter and page IN THE DATABASE (don't load the whole table). The status filter is
        // pushed to SQL as "has at least one store-slice in that status" (the rolled-up status
        // can't be computed in SQL, so this is the closest translatable predicate).
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
            query = query.Where(o => o.StoreOrders.Any(so => so.Status == parsed));

        query = query.OrderByDescending(o => o.OrderDate);

        var totalCount = await query.CountAsync();

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                CustomerEmail = o.User.Email,
                o.OrderDate,
                o.TotalAmount,
                StoreOrderCount = o.StoreOrders.Count,
                ItemCount = o.StoreOrders.Sum(so => so.StoreOrderItems.Count),
                Statuses = o.StoreOrders.Select(so => so.Status).ToList(),
                PaymentStatus = o.Payment != null ? o.Payment.Status.ToString() : null
            })
            .ToListAsync();

        // Roll up status for just this page (RollUpStatus is C#, not SQL-translatable).
        var items = rows.Select(r => new OrderSummaryResponse(
            r.Id, r.CustomerEmail, r.OrderDate, RollUpStatus(r.Statuses), r.TotalAmount,
            r.StoreOrderCount, r.ItemCount, r.PaymentStatus)).ToList();

        return new PaginatedResult<OrderSummaryResponse>
        {
            Items = items, Page = page, PageSize = pageSize,
            TotalCount = totalCount, TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<PaginatedResult<StoreOrderResponse>> GetMyStoreOrdersAsync(int sellerUserId, int page, int pageSize)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);   // M2: clamp page size

        var query = _context.StoreOrders
            .Where(so => so.Store.OwnerUserId == sellerUserId)
            .OrderByDescending(so => so.Order.OrderDate);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(so => new StoreOrderResponse(
                so.Id, so.Order.Id, so.Order.OrderDate, so.StoreId, so.Store.Name, so.Status.ToString(),
                so.SubTotal, so.CommissionAmount, so.SellerNetAmount,
                so.StoreOrderItems.Select(i => new StoreOrderItemResponse(
                    i.Id, i.ProductId, i.ProductNameSnapshot, i.Product.ImageUrl, i.UnitPrice, i.Quantity, i.Subtotal))))
            .ToListAsync();

        return new PaginatedResult<StoreOrderResponse>
        {
            Items = items, Page = page, PageSize = pageSize,
            TotalCount = totalCount, TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<OrderResponse> UpdateStoreOrderStatusAsync(
        int storeOrderId, int callerUserId, bool isAdmin, UpdateOrderStatusRequest request)
    {
        var validation = await _updateStatusValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new Buyit.Domain.Exceptions.ValidationException(errors);
        }

        var storeOrder = await _context.StoreOrders
            .Include(so => so.Store)
            .FirstOrDefaultAsync(so => so.Id == storeOrderId);
        if (storeOrder is null)
            throw new NotFoundException($"StoreOrder with ID {storeOrderId} was not found.");

        if (!isAdmin && storeOrder.Store.OwnerUserId != callerUserId)
            throw new ForbiddenException("You can only update your own store's orders.");

        var newStatus = Enum.Parse<OrderStatus>(request.Status, ignoreCase: true);
        if (!ValidProgressions[storeOrder.Status].Contains(newStatus))
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Cannot transition from {storeOrder.Status} to {newStatus}."]
            });

        if (newStatus == OrderStatus.Cancelled)
            await RestockStoreOrderAsync(storeOrderId);

        storeOrder.Status = newStatus;
        await _context.SaveChangesAsync();

        return await BuildOrderResponseAsync(storeOrder.OrderId);
    }

    public async Task<OrderResponse> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequest request)
    {
        // (1) Validate the requested status string (reuses the same validator as the per-slice path).
        var validation = await _updateStatusValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new Buyit.Domain.Exceptions.ValidationException(errors);
        }

        // (2) Load the order with all its store-slices.
        var order = await _context.Orders
            .Include(o => o.StoreOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        var newStatus = Enum.Parse<OrderStatus>(request.Status, ignoreCase: true);

        // (3) PRE-CHECK every slice: if any cannot legally reach newStatus, reject the whole call.
        var blocked = new List<string>();
        foreach (var so in order.StoreOrders)
        {
            if (so.Status == newStatus) continue;                       // already there -> nothing to do
            if (!ValidProgressions[so.Status].Contains(newStatus))
                blocked.Add($"Store-order #{so.Id} cannot transition from {so.Status} to {newStatus}.");
        }
        if (blocked.Count > 0)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = blocked.ToArray()
            });

        // (4) APPLY to every slice (all transitions are known-valid now). Restock when cancelling.
        foreach (var so in order.StoreOrders)
        {
            if (so.Status == newStatus) continue;
            if (newStatus == OrderStatus.Cancelled)
                await RestockStoreOrderAsync(so.Id);
            so.Status = newStatus;
        }

        await _context.SaveChangesAsync();
        return await BuildOrderResponseAsync(orderId);
    }

    // ---- helpers ----

    private async Task RestockStoreOrderAsync(int storeOrderId)
    {
        var items = await _context.StoreOrderItems
            .Where(soi => soi.StoreOrderId == storeOrderId)
            .Include(soi => soi.Product).ThenInclude(p => p.Inventory)
            .ToListAsync();

        foreach (var item in items)
        {
            if (item.Product.Inventory is not null)
            {
                item.Product.Inventory.QuantityInStock += item.Quantity;
                item.Product.Inventory.LastUpdated = DateTime.UtcNow;
            }
        }
    }

    private Task<Order?> LoadOrderGraphAsync(int orderId) =>
        _context.Orders
            .Include(o => o.Payment)
            .Include(o => o.StoreOrders).ThenInclude(so => so.Store)
            .Include(o => o.StoreOrders).ThenInclude(so => so.StoreOrderItems).ThenInclude(soi => soi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

    private async Task<OrderResponse> BuildOrderResponseAsync(int orderId)
    {
        var order = await LoadOrderGraphAsync(orderId);
        if (order is null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");
        return MapToOrderResponse(order);
    }

    private static OrderResponse MapToOrderResponse(Order order) => new(
        order.Id,
        order.OrderDate,
        RollUpStatus(order.StoreOrders.Select(so => so.Status)),
        order.TotalAmount,
        order.DiscountAmount,
        order.ShippingLine1, order.ShippingLine2, order.ShippingCity, order.ShippingPostalCode,
        order.ShippingState, order.ShippingCountry,
        order.Payment != null ? order.Payment.Status.ToString() : null,
        order.StoreOrders.Select(so => new StoreOrderResponse(
            so.Id, order.Id, order.OrderDate, so.StoreId, so.Store?.Name ?? string.Empty, so.Status.ToString(),
            so.SubTotal, so.CommissionAmount, so.SellerNetAmount,
            so.StoreOrderItems.Select(i => new StoreOrderItemResponse(
                i.Id, i.ProductId, i.ProductNameSnapshot, i.Product?.ImageUrl, i.UnitPrice, i.Quantity, i.Subtotal))))
        );

    // M2: clamp paging so a caller can't request an unbounded page (resource-exhaustion DoS).
    private const int MaxPageSize = 50;
    private static (int page, int pageSize) NormalizePaging(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize < 1 ? 10 : pageSize, 1, MaxPageSize));

    // Derive one status for the parent order from its store-slices.
    private static string RollUpStatus(IEnumerable<OrderStatus> storeStatuses)
    {
        var list = storeStatuses.ToList();
        if (list.Count == 0) return OrderStatus.Pending.ToString();
        if (list.All(s => s == OrderStatus.Cancelled)) return OrderStatus.Cancelled.ToString();

        // Ignore cancelled slices, then report the EARLIEST remaining stage
        // (enum order: Pending < Confirmed < Shipped < Delivered).
        var active = list.Where(s => s != OrderStatus.Cancelled).ToList();
        return active.Min().ToString();
    }
}

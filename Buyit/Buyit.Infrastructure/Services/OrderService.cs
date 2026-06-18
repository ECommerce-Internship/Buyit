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

    public async Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request)
    {
        // Validate shipping address fields before any DB work
        var validationResult = await _placeOrderValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errorDictionary = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );
            throw new Buyit.Domain.Exceptions.ValidationException(errorDictionary);
        }

        // (1) Fetch cart with items for this user
        var cart = await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Inventory)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null || !cart.CartItems.Any())
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["cart"] = ["Cart is empty. Add items before placing an order."]
            });

        // (2) Check stock for ALL items upfront, collect all failures before throwing
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

        // (3) Begin database transaction — all changes succeed or all roll back
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // (4) Compute final total, re-validating coupon at order time
            var subtotal = cart.CartItems.Sum(ci => ci.Product.Price * ci.Quantity);

            var coupon = cart.Coupon;
            if (coupon != null && (!coupon.IsActive || coupon.ExpiryDate < DateTime.UtcNow))
            {
                // Throw so customer knows their coupon is no longer valid
                throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
                {
                    ["coupon"] = [$"Coupon '{coupon.Code}' is no longer valid. Please remove it from your cart and try again."]
                });
            }

            var discountPercentage = coupon?.DiscountPercentage ?? 0;
            var discountAmount = Math.Round(subtotal * (discountPercentage / 100), 2);
            var finalTotal = subtotal - discountAmount;

            // (5) Create Order entity
            var order = new Order
            {
                UserId = userId,
                Status = OrderStatus.Pending,
                TotalAmount = finalTotal,
                ShippingLine1 = request.ShippingLine1,
                ShippingLine2 = request.ShippingLine2,
                ShippingCity = request.ShippingCity,
                ShippingPostalCode = request.ShippingPostalCode,
                ShippingCountry = request.ShippingCountry
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // (6) Create OrderItems with price snapshot and deduct inventory
            foreach (var item in cart.CartItems)
            {
                // Price snapshot — captures price at purchase time
                _context.OrderItems.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Product.Price
                });

                // (7) Deduct stock
                item.Product.Inventory!.QuantityInStock -= item.Quantity;
                item.Product.Inventory.LastUpdated = DateTime.UtcNow;
            }

            // Capture affected product ids BEFORE clearing the cart — after RemoveRange +
            // SaveChanges, EF may detach these items from cart.CartItems, so read them now.
            var affectedProductIds = cart.CartItems.Select(ci => ci.ProductId).Distinct().ToList();

            // (8) Clear cart items and remove coupon
            _context.CartItems.RemoveRange(cart.CartItems);
            cart.CouponId = null;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // (8b) Stock just dropped for every ordered product, and stock is embedded in the
            //      cached ProductResponse. Invalidate each affected product AFTER the commit so
            //      GET /products(/{id}) can't keep showing "in stock" for something just sold out.
            //      (Cache calls are fail-open, so a Redis outage won't affect the placed order.)
            foreach (var productId in affectedProductIds)
                await _cache.InvalidateProductAsync(productId);

            // (9) Fire and forget — queue confirmation email without blocking the response
            var userEmail = (await _context.Users.FindAsync(userId))?.Email ?? string.Empty;
            _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, userEmail, order.TotalAmount));

            // Reload order with items for response
            await _context.Entry(order).Collection(o => o.OrderItems).Query()
                .Include(oi => oi.Product).LoadAsync();

            return MapToOrderResponse(order);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET MY ORDERS: Paginated list for current user ordered by OrderDate descending
    public async Task<PaginatedResult<OrderSummaryResponse>> GetMyOrdersAsync(int userId, int page, int pageSize)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.OrderDate);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryResponse(
                o.Id,
                o.OrderDate,
                o.Status.ToString(),
                o.TotalAmount,
                o.OrderItems.Count,
                o.Payment != null ? o.Payment.Status.ToString() : null
            ))
            .ToListAsync();

        return new PaginatedResult<OrderSummaryResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    // GET ORDER BY ID: Full order detail — validates ownership or admin access
    public async Task<OrderResponse> GetOrderByIdAsync(int orderId, int userId, bool isAdmin)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        // Only the owner or an admin can view the order
        if (!isAdmin && order.UserId != userId)
            throw new ForbiddenException("You do not have permission to view this order.");

        return MapToOrderResponse(order);
    }

    // CANCEL ORDER: Only allowed if status is Pending
    public async Task CancelOrderAsync(int orderId, int userId)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (order.UserId != userId)
            throw new ForbiddenException("You do not have permission to cancel this order.");

        if (order.Status != OrderStatus.Pending)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Order cannot be cancelled because it is already {order.Status}. Only Pending orders can be cancelled."]
            });

        order.Status = OrderStatus.Cancelled;
        await _context.SaveChangesAsync();
    }

    // GET ALL ORDERS (ADMIN): Paginated, optional filter by status and date range
    public async Task<PaginatedResult<OrderSummaryResponse>> GetAllOrdersAsync(
        int page, int pageSize, string? status, DateTime? from, DateTime? to)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .AsQueryable();

        // Apply optional status filter
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(o => o.Status == parsedStatus);

        // Apply optional date range filter
        if (from.HasValue)
            query = query.Where(o => o.OrderDate >= from.Value);

        if (to.HasValue)
            query = query.Where(o => o.OrderDate <= to.Value);

        query = query.OrderByDescending(o => o.OrderDate);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryResponse(
                o.Id,
                o.OrderDate,
                o.Status.ToString(),
                o.TotalAmount,
                o.OrderItems.Count,
                o.Payment != null ? o.Payment.Status.ToString() : null
            ))
            .ToListAsync();

        return new PaginatedResult<OrderSummaryResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    // UPDATE ORDER STATUS (ADMIN): Validates status is a valid progression
    public async Task<OrderResponse> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequest request)
    {
        // Validate request
        var validationResult = await _updateStatusValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errorDictionary = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );
            throw new Buyit.Domain.Exceptions.ValidationException(errorDictionary);
        }

        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        var newStatus = Enum.Parse<OrderStatus>(request.Status, ignoreCase: true);

        // Validate status progression — must move forward, cannot reactivate cancelled orders
        var validProgressions = new Dictionary<OrderStatus, List<OrderStatus>>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };

        if (!validProgressions[order.Status].Contains(newStatus))
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Cannot transition order from {order.Status} to {newStatus}."]
            });

        order.Status = newStatus;
        await _context.SaveChangesAsync();

        return MapToOrderResponse(order);
    }

    // Shared mapping method — avoids duplicating the OrderResponse construction
    private static OrderResponse MapToOrderResponse(Order order) => new(
        order.Id,
        order.OrderDate,
        order.Status.ToString(),
        order.TotalAmount,
        order.ShippingLine1,
        order.ShippingLine2,
        order.ShippingCity,
        order.ShippingPostalCode,
        order.ShippingCountry,
        order.OrderItems.Select(oi => new OrderItemResponse(
            oi.Id,
            oi.ProductId,
            oi.Product.Name,
            oi.Product.Sku,
            oi.UnitPrice,
            oi.Quantity,
            oi.UnitPrice * oi.Quantity
        ))
    );
}

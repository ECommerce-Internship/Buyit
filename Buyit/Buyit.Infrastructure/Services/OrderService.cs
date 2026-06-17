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

    public OrderService(AppDbContext context, IEmailService emailService, IValidator<PlaceOrderRequest> placeOrderValidator)
    {
        _context = context;
        _emailService = emailService;
        _placeOrderValidator = placeOrderValidator;
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
            // (4) Compute final total re-validating coupon at order time in case it expired or was deactivated 
            var subtotal = cart.CartItems.Sum(ci => ci.Product.Price * ci.Quantity);

            var coupon = cart.Coupon;
            if (coupon != null && (!coupon.IsActive || coupon.ExpiryDate < DateTime.UtcNow))
            {
                cart.CouponId = null;
                coupon = null;
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

            // (8) Clear cart items and remove coupon
            _context.CartItems.RemoveRange(cart.CartItems);
            cart.CouponId = null;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // (9) Fire and forget — queue confirmation email without blocking the response
            var userEmail = (await _context.Users.FindAsync(userId))?.Email ?? string.Empty;
            _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, userEmail, order.TotalAmount));

            // Reload order items with product details for response
            var orderItems = await _context.OrderItems
                .Include(oi => oi.Product)
                .Where(oi => oi.OrderId == order.Id)
                .ToListAsync();

            return new OrderResponse(
                order.Id,
                order.OrderDate,
                order.Status.ToString(),
                order.TotalAmount,
                order.ShippingLine1,
                order.ShippingLine2,
                order.ShippingCity,
                order.ShippingPostalCode,
                order.ShippingCountry,
                orderItems.Select(oi => new OrderItemResponse(
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
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
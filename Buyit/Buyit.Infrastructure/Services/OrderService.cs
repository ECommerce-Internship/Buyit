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

  ──── (224 lines hidden) ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
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
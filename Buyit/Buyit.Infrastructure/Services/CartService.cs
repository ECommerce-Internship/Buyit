using Microsoft.EntityFrameworkCore;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;

namespace Buyit.Infrastructure.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _context;

    public CartService(AppDbContext context)
    {
        _context = context;
    }

    // GET CART: Fetches cart with items and applies coupon discount if there is one
    public async Task<CartResponse> GetCartAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        return BuildCartResponse(cart);
    }

    // ADD ITEM: Finds or creates cart + validates stock + upserts cart item
    public async Task<CartResponse> AddItemAsync(int userId, AddCartItemRequest request)
    {
        // Lower-bound guard: quantity must be positive. Without this, a zero/negative quantity
        // (e.g. from an LLM-driven add_to_cart) would slip past the stock check and persist a
        // corrupt cart line. Guards both the HTTP and MCP paths since both call this method.
        if (request.Quantity < 1)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["quantity"] = ["Quantity must be at least 1."]
            });

        var cart = await GetOrCreateCartAsync(userId);

        // Check product exists and is not soft-deleted
        var product = await _context.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted);

        if (product == null)
            throw new NotFoundException($"Product with ID {request.ProductId} was not found.");

        // Check sufficient stock
        var availableStock = product.Inventory?.QuantityInStock ?? 0;
        if (availableStock < request.Quantity)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["quantity"] = [$"Insufficient stock for '{product.Name}'. Available: {availableStock}, Requested: {request.Quantity}."]
            });

        // Upsert: update quantity if item already in cart, otherwise add new
        var existingItem = cart.CartItems.FirstOrDefault(ci => ci.ProductId == request.ProductId);
        if (existingItem != null)
        {
            var newQuantity = existingItem.Quantity + request.Quantity;
            if (availableStock < newQuantity)
                throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
                {
                    ["quantity"] = [$"Insufficient stock for '{product.Name}'. Available: {availableStock}, In cart: {existingItem.Quantity}, Requested: {request.Quantity}."]
                });

            existingItem.Quantity = newQuantity;
        }
        else
        {
            cart.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            });
        }

        await _context.SaveChangesAsync();

        // Reload cart with full product details for response
        await ReloadCartAsync(cart);
        return BuildCartResponse(cart);
    }

    // UPDATE ITEM: Updates quantity by productId with stock check
    public async Task<CartResponse> UpdateItemAsync(int userId, int productId, UpdateCartItemRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var cartItem = cart.CartItems.FirstOrDefault(ci => ci.ProductId == productId);
        if (cartItem == null)
            throw new NotFoundException($"Product with ID {productId} was not found in the cart.");

        var product = await _context.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);

        if (product == null)
            throw new NotFoundException($"Product with ID {productId} was not found.");

        var availableStock = product.Inventory?.QuantityInStock ?? 0;
        if (availableStock < request.Quantity)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["quantity"] = [$"Insufficient stock for '{product.Name}'. Available: {availableStock}, Requested: {request.Quantity}."]
            });

        cartItem.Quantity = request.Quantity;
        await _context.SaveChangesAsync();

        await ReloadCartAsync(cart);
        return BuildCartResponse(cart);
    }

    // REMOVE ITEM: Deletes a single cart item by productId
    public async Task RemoveItemAsync(int userId, int productId)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var cartItem = cart.CartItems.FirstOrDefault(ci => ci.ProductId == productId);
        if (cartItem == null)
            throw new NotFoundException($"Product with ID {productId} was not found in the cart.");

        _context.CartItems.Remove(cartItem);
        await _context.SaveChangesAsync();
    }

    // CLEAR CART: Removes all items and clears any applied coupon
    public async Task ClearCartAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);

        _context.CartItems.RemoveRange(cart.CartItems);
        cart.CouponId = null;

        await _context.SaveChangesAsync();
    }

    // APPLY COUPON: Validates coupon is active and not expired, then sets on cart
    public async Task<CartResponse> ApplyCouponAsync(int userId, ApplyCouponRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var coupon = await _context.Coupons
            .FirstOrDefaultAsync(c => c.Code.ToLower() == request.Code.ToLower());

        if (coupon == null)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = [$"Coupon '{request.Code}' does not exist."]
            });

        if (!coupon.IsActive)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = [$"Coupon '{request.Code}' is no longer active."]
            });

        if (coupon.ExpiryDate < DateTime.UtcNow)
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = [$"Coupon '{request.Code}' has expired."]
            });

        cart.CouponId = coupon.Id;
        await _context.SaveChangesAsync();

        await ReloadCartAsync(cart);
        return BuildCartResponse(cart);
    }

    // REMOVE COUPON
    public async Task<CartResponse> RemoveCouponAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);

        cart.CouponId = null;
        await _context.SaveChangesAsync();

        await ReloadCartAsync(cart);
        return BuildCartResponse(cart);
    }

    // Finds existing cart for user or creates a new one
    private async Task<Cart> GetOrCreateCartAsync(int userId)
    {
        var cart = await _context.Carts
        .Include(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
                .ThenInclude(p => p.Store)
        .Include(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
                .ThenInclude(p => p.Inventory)
        .Include(c => c.Coupon)
        .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
        }

        return cart;
    }

    // Reloads cart navigation properties after a SaveChanges
    private async Task ReloadCartAsync(Cart cart)
    {
        await _context.Entry(cart)
            .Collection(c => c.CartItems)
            .Query()
            .Include(ci => ci.Product).ThenInclude(p => p.Store)
            .Include(ci => ci.Product).ThenInclude(p => p.Inventory)
            .LoadAsync();

        await _context.Entry(cart)
            .Reference(c => c.Coupon)
            .LoadAsync();
    }

    // Builds CartResponse DTO with subtotal, discount, and final total calculations
    private static CartResponse BuildCartResponse(Cart cart)
    {
        var items = cart.CartItems
        .Where(ci => ci.Product != null)
        .Select(ci => new CartItemResponse(
            ci.Id,
            ci.ProductId,
            ci.Product.Name,
            ci.Product.Sku,
            ci.Product.Price,
            ci.Quantity,
            ci.Product.Price * ci.Quantity,
            ci.Product.StoreId,
            ci.Product.Store.Name,
            ci.Product.Inventory?.QuantityInStock ?? 0
        )).ToList();

        var subtotal = items.Sum(i => i.LineTotal);
        var discountPercentage = cart.Coupon?.DiscountPercentage ?? 0;
        var discountAmount = Math.Round(subtotal * (discountPercentage / 100), 2);
        var finalTotal = subtotal - discountAmount;

        return new CartResponse(
            cart.Id,
            items,
            subtotal,
            cart.Coupon?.Code,
            discountPercentage,
            discountAmount,
            finalTotal
        );
    }
}
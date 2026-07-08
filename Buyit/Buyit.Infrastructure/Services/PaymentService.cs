using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Buyit.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    // Upper bound on admin page size to avoid resource-exhaustion (CWE-770).
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IValidator<ProcessPaymentRequest> _validator;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext context,
        IEmailService emailService,
        IValidator<ProcessPaymentRequest> validator,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _emailService = emailService;
        _validator = validator;
        _logger = logger;
    }

    public async Task<PaymentResponse> ProcessPaymentAsync(int userId, ProcessPaymentRequest request)
    {
        var validationResult = await _validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new Buyit.Domain.Exceptions.ValidationException(errors);
        }

        var order = await _context.Orders
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {request.OrderId} was not found.");

        if (order.UserId != userId)
            throw new ForbiddenException("You do not have permission to pay for this order.");

        if (order.Payment != null && order.Payment.Status == PaymentStatus.Paid)
            throw new ConflictException("This order has already been paid.");

        var isFailTest = string.Equals(request.PaymentMethod, "FailTest", StringComparison.OrdinalIgnoreCase);

        PaymentMethod method;
        if (isFailTest)
        {
            // "FailTest" is a test-only trigger, not a real PaymentMethod. Store a
            // placeholder so the (non-nullable) Method column holds a valid enum value;
            // the resulting Status will be Failed.
            method = PaymentMethod.CreditCard;
        }
        else if (!Enum.TryParse(request.PaymentMethod, ignoreCase: true, out method))
        {
            throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["paymentMethod"] = ["Payment method must be CreditCard, DebitCard, PayPal, or FailTest."]
            });
        }

        var status = isFailTest ? PaymentStatus.Failed : PaymentStatus.Paid;

        var payment = order.Payment ?? new Payment { OrderId = order.Id };
        payment.Amount = order.TotalAmount;
        payment.Method = method;
        payment.Status = status;
        payment.TransactionId = Guid.NewGuid().ToString("N");
        payment.PaidAt = status == PaymentStatus.Paid ? DateTime.UtcNow : null;

        if (payment.Id == 0)
            _context.Payments.Add(payment);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Unique index on Payment.OrderId: a concurrent request already created the
            // payment row. Surface the business rule (409) instead of a raw 500.
            throw new ConflictException("This order has already been paid.");
        }

        _logger.LogInformation(
            "Payment {PaymentId} for Order {OrderId} processed with status {Status}",
            payment.Id, order.Id, status);

        if (status == PaymentStatus.Paid)
        {
            var email = (await _context.Users.FindAsync(userId))?.Email ?? string.Empty;
            _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, email, order.TotalAmount));
        }

        return MapToResponse(payment, order.Id);
    }

    public async Task<PaymentResponse> GetByOrderIdAsync(int orderId, int userId, bool isAdmin)
    {
        var order = await _context.Orders
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (!isAdmin && order.UserId != userId)
            throw new ForbiddenException("You do not have permission to view this payment.");

        if (order.Payment == null)
            throw new NotFoundException($"No payment found for order {orderId}.");

        return MapToResponse(order.Payment, order.Id);
    }

    public async Task<PaymentResponse> RefundAsync(int paymentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Order)
                .ThenInclude(o => o.StoreOrders)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment == null)
            throw new NotFoundException($"Payment with ID {paymentId} was not found.");

        if (payment.Status != PaymentStatus.Paid)
            throw new ConflictException($"Only paid payments can be refunded. Current status: {payment.Status}.");

        payment.Status = PaymentStatus.Refunded;
        // Marketplace: a refund cancels every store-slice of the order.
        foreach (var so in payment.Order.StoreOrders)
            so.Status = OrderStatus.Cancelled;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Payment {PaymentId} refunded; Order {OrderId} set to Cancelled",
            payment.Id, payment.OrderId);

        return MapToResponse(payment, payment.OrderId);
    }

    public async Task<PaginatedResult<PaymentResponse>> GetAllPaymentsAsync(int page, int pageSize, string? status)
    {
        // Guard against invalid/huge paging values (negative OFFSET errors, resource exhaustion).
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Payments.AsQueryable();

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<PaymentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(p => p.Status == parsedStatus);
        }

        query = query.OrderByDescending(p => p.Id);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentResponse(
                p.Id, p.OrderId, p.Amount,
                p.Method.ToString(), p.Status.ToString(),
                p.TransactionId, p.PaidAt,
                p.Order.User.FirstName + " " + p.Order.User.LastName,
                p.Order.User.Email))
            .ToListAsync();

        return new PaginatedResult<PaymentResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static PaymentResponse MapToResponse(Payment payment, int orderId) => new(
        payment.Id,
        orderId,
        payment.Amount,
        payment.Method.ToString(),
        payment.Status.ToString(),
        payment.TransactionId,
        payment.PaidAt
    );
}
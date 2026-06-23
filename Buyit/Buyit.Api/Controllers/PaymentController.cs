using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using System.Security.Claims;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
[Authorize(Roles = "Customer")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    // Reads the logged-in user's id from the JWT "sub" claim.
    private int GetUserId() => int.Parse(User.FindFirstValue("sub")!);

    // POST api/v1/payments  → pay for one of my orders
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
    {
        var payment = await _paymentService.ProcessPaymentAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetPaymentByOrderId), new { orderId = payment.OrderId }, payment);
    }

    // GET api/v1/payments/{orderId}  → view the payment for one of my orders
    [HttpGet("{orderId:int}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentByOrderId(int orderId)
    {
        var payment = await _paymentService.GetByOrderIdAsync(orderId, GetUserId(), isAdmin: false);
        return Ok(payment);
    }
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/payments")]
[Authorize(Roles = "Admin")]
public class AdminPaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public AdminPaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    // GET api/v1/admin/payments  → paginated list, optional ?status= filter
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<PaymentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null)
    {
        var payments = await _paymentService.GetAllPaymentsAsync(page, pageSize, status);
        return Ok(payments);
    }

    // POST api/v1/admin/payments/{id}/refund  → refund a paid payment
    [HttpPost("{id:int}/refund")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refund(int id)
    {
        var payment = await _paymentService.RefundAsync(id);
        return Ok(payment);
    }
}
using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>The payment record for an order (one-to-one with Order).</summary>
public class Payment
{
    public int Id { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    // Set once the payment succeeds; null while pending/failed.
    public DateTime? PaidAt { get; set; }

    // FK + a unique index (added in Step 3) gives the one-to-one link to Order.
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}

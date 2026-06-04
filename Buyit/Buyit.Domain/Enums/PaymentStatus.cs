namespace Buyit.Domain.Enums;

/// <summary>Tracks where a payment is in its lifecycle.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Refunded = 3
}

namespace Buyit.Application.DTOs;

public record ProcessPaymentRequest
(
    int OrderId,
    string PaymentMethod
);
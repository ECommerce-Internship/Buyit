using FluentValidation;
using Buyit.Application.DTOs;
namespace Buyit.Application.Validators;

public class ProcessPaymentRequestValidator : AbstractValidator<ProcessPaymentRequest>
{
    public ProcessPaymentRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .GreaterThan(0).WithMessage("OrderId must be a positive number.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("Payment method is required.");
    }
}
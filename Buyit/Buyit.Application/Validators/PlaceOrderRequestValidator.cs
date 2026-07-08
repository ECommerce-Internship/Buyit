using FluentValidation;
using Buyit.Application.DTOs;

namespace Buyit.Application.Validators;

public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.ShippingLine1)
            .NotEmpty().WithMessage("Shipping address line 1 is required.")
            .MaximumLength(200).WithMessage("Shipping address line 1 must not exceed 200 characters.");

        RuleFor(x => x.ShippingLine2)
            .MaximumLength(200).WithMessage("Shipping address line 2 must not exceed 200 characters.")
            .When(x => x.ShippingLine2 != null);

        RuleFor(x => x.ShippingCity)
        .NotEmpty().WithMessage("City is required.")
        .MaximumLength(100).WithMessage("City must not exceed 100 characters.");

        RuleFor(x => x.ShippingState)
            .NotEmpty().WithMessage("State / region is required.")
            .MaximumLength(100).WithMessage("State / region must not exceed 100 characters.");

        RuleFor(x => x.ShippingPostalCode)
            .NotEmpty().WithMessage("Postal code is required.")
            .MaximumLength(20).WithMessage("Postal code must not exceed 20 characters.");

        RuleFor(x => x.ShippingCountry)
            .NotEmpty().WithMessage("Country is required.")
            .MaximumLength(100).WithMessage("Country must not exceed 100 characters.");
    }
}
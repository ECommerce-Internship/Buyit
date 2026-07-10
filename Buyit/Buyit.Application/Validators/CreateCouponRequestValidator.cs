using Buyit.Application.DTOs;
using Buyit.Domain.Enums;
using FluentValidation;

namespace Buyit.Application.Validators;

public class CreateCouponRequestValidator : AbstractValidator<CreateCouponRequest>
{
    public CreateCouponRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(50).WithMessage("Code must be 50 characters or fewer.");

        RuleFor(x => x.DiscountType)
            .IsInEnum().WithMessage("DiscountType must be Percentage or FixedAmount.");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("DiscountValue must be a positive amount.");

        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.DiscountType == CouponDiscountType.Percentage)
            .WithMessage("A percentage discount cannot exceed 100.");

        RuleFor(x => x.ExpiryDate)
            .GreaterThan(DateTime.UtcNow).WithMessage("ExpiryDate must be in the future.");

        RuleFor(x => x.UsageLimit)
            .GreaterThanOrEqualTo(1)
            .When(x => x.UsageLimit is not null)
            .WithMessage("UsageLimit must be at least 1 when provided.");
    }
}
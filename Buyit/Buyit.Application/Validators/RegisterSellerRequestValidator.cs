using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

/// <summary>
/// Rules a RegisterSellerRequest must satisfy. The user fields mirror RegisterRequestValidator
/// so the seller path enforces the same email/password policy as the customer path.
/// </summary>
public class RegisterSellerRequestValidator : AbstractValidator<RegisterSellerRequest>
{
    public RegisterSellerRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100);

        RuleFor(x => x.StoreName)
            .NotEmpty().WithMessage("Store name is required.")
            .MaximumLength(150);

        RuleFor(x => x.StoreDescription)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrWhiteSpace(x.StoreDescription));
    }
}

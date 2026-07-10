using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Reset code is required.")
            .Matches("^[0-9]{6}$").WithMessage("Reset code must be exactly 6 digits.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("New password must be at least 8 characters.");
    }
}
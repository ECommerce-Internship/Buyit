using System;
using System.Collections.Generic;
using System.Text;
using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.");

        // Phone is OPTIONAL at registration: only validate format/length WHEN one is provided.
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number must be at most 30 characters.")
            .Matches(@"^[0-9+\-\s()]*$").WithMessage("Phone number may contain only digits, spaces, +, -, and ().")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}

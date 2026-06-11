using System;
using System.Collections.Generic;
using System.Text;
using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must be at most 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must be at most 100 characters.");

        // Phone is OPTIONAL: only validate format/length WHEN the user actually sent one.
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number must be at most 30 characters.")
            .Matches(@"^[0-9+\-\s()]*$").WithMessage("Phone number may contain only digits, spaces, +, -, and ().")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}
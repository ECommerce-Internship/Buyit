using FluentValidation;
using Buyit.Application.DTOs;

namespace Buyit.Application.Validators;

// TB-47: rules for the generate-content request. Specs is required and capped at 500 chars
// (the Jira description specifies "string required, max 500 characters, comma-separated").
public class GenerateContentRequestValidator
    : AbstractValidator<GenerateContentRequest>
{
    public GenerateContentRequestValidator()
    {
        RuleFor(x => x.Specs)
            .NotEmpty().WithMessage("Specs are required.")
            .MaximumLength(500).WithMessage("Specs cannot exceed 500 characters.");
    }
}
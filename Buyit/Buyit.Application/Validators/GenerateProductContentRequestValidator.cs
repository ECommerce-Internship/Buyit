using FluentValidation;
using Buyit.Application.DTOs;

namespace Buyit.Application.Validators;

public class GenerateProductContentRequestValidator
    : AbstractValidator<GenerateProductContentRequest>
{
    public GenerateProductContentRequestValidator()
    {
        RuleFor(x => x.ProductName)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(200).WithMessage("Product name cannot exceed 200 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .MaximumLength(100).WithMessage("Category cannot exceed 100 characters.");

        RuleFor(x => x.Specs)
            .NotEmpty().WithMessage("Specs are required.")
            .MaximumLength(2000).WithMessage("Specs cannot exceed 2000 characters.");
    }
}
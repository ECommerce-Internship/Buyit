using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required.")
            .MaximumLength(100).WithMessage("Category name cannot exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");

        RuleFor(x => x.ParentCategoryId)
            .Must(id => id == null || id > 0)
            .WithMessage("ParentCategoryId must be a valid positive number if provided.");
    }
}

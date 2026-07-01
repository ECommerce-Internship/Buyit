using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Description)
            .MaximumLength(1000);

        RuleFor(x => x.ParentCategoryId)
            .Must(id => id == null || id > 0);
    }
}
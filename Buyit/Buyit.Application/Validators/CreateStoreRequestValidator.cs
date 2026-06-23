using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

/// <summary>
/// Rules for opening a store. Lengths mirror the Store entity's column limits so an
/// over-length value is reported as a clean 400, not a database 500.
/// </summary>
public class CreateStoreRequestValidator : AbstractValidator<CreateStoreRequest>
{
    public CreateStoreRequestValidator()
    {
        RuleFor(x => x.StoreName)
            .NotEmpty().WithMessage("Store name is required.")
            .MaximumLength(150).WithMessage("Store name must be at most 150 characters.");

        RuleFor(x => x.StoreDescription)
            .MaximumLength(1000).WithMessage("Store description must be at most 1000 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.StoreDescription));
    }
}

using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

/// <summary>Rules a CreateProductRequest must satisfy before we try to create a product.</summary>
public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(200).WithMessage("Product name must be at most 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must be at most 2000 characters.");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required.")
            .MaximumLength(50).WithMessage("SKU must be at most 50 characters.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than 0.");

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).WithMessage("A valid category id is required.");

        RuleFor(x => x.StoreId)
            .GreaterThan(0).WithMessage("A valid StoreId is required.");

        RuleFor(x => x.InitialStock)
            .GreaterThanOrEqualTo(0).WithMessage("Initial stock cannot be negative.");

        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048).WithMessage("Image URL must be at most 2048 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));
    }
}
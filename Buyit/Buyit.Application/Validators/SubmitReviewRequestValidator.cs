using FluentValidation;
using Buyit.Application.DTOs;

namespace Buyit.Application.Validators;

public class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    public SubmitReviewRequestValidator()
    {
        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("Rating must be between 1 and 5.");

        // Comment is optional. Only enforce the length rule when it is provided.
        RuleFor(x => x.Comment)
            .MaximumLength(1000)
            .WithMessage("Comment cannot exceed 1000 characters.")
            .When(x => x.Comment is not null);
    }
}
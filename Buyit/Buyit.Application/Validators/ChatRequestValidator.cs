using Buyit.Application.DTOs;
using FluentValidation;

namespace Buyit.Application.Validators;

public class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.message)
            .NotEmpty().WithMessage("Message must not be empty.")
            .MaximumLength(2000).WithMessage("Message must be 2000 characters or fewer.");
    }
}

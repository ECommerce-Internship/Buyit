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

        RuleFor(x => x.conversationId)
            .Must(BeAValidGuid).WithMessage("conversationId must be a valid GUID.")
            .When(x => !string.IsNullOrWhiteSpace(x.conversationId));
    }

    // Returns true when the string parses as a GUID. Guid.TryParse never throws — it just reports
    // success/failure — which is exactly what a validation predicate needs.
    private static bool BeAValidGuid(string? value) => Guid.TryParse(value, out _);
}

using FluentValidation;

namespace Titan.API.Validators;

/// <summary>
/// Request to invalidate a session by ticket ID.
/// </summary>
public record InvalidateSessionRequest(string TicketId);

/// <summary>
/// Validates InvalidateSessionRequest.
/// </summary>
public class InvalidateSessionRequestValidator : AbstractValidator<InvalidateSessionRequest>
{
    public InvalidateSessionRequestValidator()
    {
        RuleFor(x => x.TicketId)
            .NotEmpty().WithMessage("Ticket ID is required")
            .MinimumLength(10).WithMessage("Ticket ID must be at least 10 characters")
            .MaximumLength(200).WithMessage("Ticket ID must not exceed 200 characters")
            .Must(id => !id.Any(char.IsControl)).WithMessage("Ticket ID contains invalid characters");
    }
}

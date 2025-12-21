using FluentValidation;
using Titan.Abstractions.Models;

namespace Titan.API.Validators;

public class SendBroadcastRequestValidator : AbstractValidator<Controllers.SendBroadcastRequest>
{
    public SendBroadcastRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required")
            .MaximumLength(2000).WithMessage("Content must not exceed 2000 characters");

        RuleFor(x => x.Title)
            .MaximumLength(100).WithMessage("Title must not exceed 100 characters")
            .When(x => x.Title != null);

        RuleFor(x => x.IconId)
            .MaximumLength(100).WithMessage("IconId must not exceed 100 characters")
            .When(x => x.IconId != null);

        RuleFor(x => x.DurationSeconds)
            .InclusiveBetween(1, 3600).WithMessage("Duration must be between 1 and 3600 seconds")
            .When(x => x.DurationSeconds.HasValue);

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid message type");
    }
}

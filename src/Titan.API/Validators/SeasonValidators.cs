using FluentValidation;
using Titan.API.Controllers;

namespace Titan.API.Validators;

public class CreateSeasonRequestValidator : AbstractValidator<CreateSeasonRequest>
{
    public CreateSeasonRequestValidator()
    {
        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]+$").WithMessage("SeasonId must contain only alphanumeric characters, underscores, hyphens, or periods");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters");

        RuleFor(x => x.MigrationTargetId)
            .MaximumLength(100).WithMessage("MigrationTargetId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]*$").WithMessage("MigrationTargetId must contain only alphanumeric characters, underscores, hyphens, or periods")
            .When(x => !string.IsNullOrEmpty(x.MigrationTargetId));

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("StartDate is required");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("EndDate must be after StartDate")
            .When(x => x.EndDate.HasValue);
    }
}

public class UpdateSeasonStatusRequestValidator : AbstractValidator<UpdateSeasonStatusRequest>
{
    public UpdateSeasonStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid season status");
    }
}

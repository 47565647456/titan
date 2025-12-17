using FluentValidation;
using Titan.API.Controllers;

namespace Titan.API.Validators;

public class CreateBaseTypeRequestValidator : AbstractValidator<CreateBaseTypeRequest>
{
    public CreateBaseTypeRequestValidator()
    {
        RuleFor(x => x.BaseTypeId)
            .NotEmpty().WithMessage("BaseTypeId is required")
            .MaximumLength(100).WithMessage("BaseTypeId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]+$").WithMessage("BaseTypeId must contain only alphanumeric characters, underscores, hyphens, or periods");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Width)
            .InclusiveBetween(1, 100).WithMessage("Width must be between 1 and 100");

        RuleFor(x => x.Height)
            .InclusiveBetween(1, 100).WithMessage("Height must be between 1 and 100");

        RuleFor(x => x.MaxStackSize)
            .InclusiveBetween(1, 9999).WithMessage("MaxStackSize must be between 1 and 9999");
    }
}

public class UpdateBaseTypeRequestValidator : AbstractValidator<UpdateBaseTypeRequest>
{
    public UpdateBaseTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Width)
            .InclusiveBetween(1, 100).WithMessage("Width must be between 1 and 100");

        RuleFor(x => x.Height)
            .InclusiveBetween(1, 100).WithMessage("Height must be between 1 and 100");

        RuleFor(x => x.MaxStackSize)
            .InclusiveBetween(1, 9999).WithMessage("MaxStackSize must be between 1 and 9999");
    }
}

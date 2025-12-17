using FluentValidation;
using Titan.API.Controllers;

namespace Titan.API.Validators;

public class AdminLoginRequestValidator : AbstractValidator<AdminLoginRequest>
{
    public AdminLoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters")
            .EmailAddress().WithMessage("Email must be a valid email address");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters");
    }
}

public class CreateAdminUserRequestValidator : AbstractValidator<CreateAdminUserRequest>
{
    public CreateAdminUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters")
            .EmailAddress().WithMessage("Email must be a valid email address");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters");

        RuleFor(x => x.DisplayName)
            .MaximumLength(200).WithMessage("Display name must not exceed 200 characters")
            .When(x => x.DisplayName != null);

        RuleForEach(x => x.Roles)
            .NotEmpty().WithMessage("Role name cannot be empty")
            .MaximumLength(50).WithMessage("Role name must not exceed 50 characters");
    }
}

public class UpdateAdminUserRequestValidator : AbstractValidator<UpdateAdminUserRequest>
{
    public UpdateAdminUserRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .MaximumLength(200).WithMessage("Display name must not exceed 200 characters")
            .When(x => x.DisplayName != null);

        RuleForEach(x => x.Roles)
            .NotEmpty().WithMessage("Role name cannot be empty")
            .MaximumLength(50).WithMessage("Role name must not exceed 50 characters");
    }
}

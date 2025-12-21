using FluentValidation;

namespace Titan.API.Validators;

public class LoginRequestValidator : AbstractValidator<Controllers.LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required")
            .MaximumLength(4096).WithMessage("Token must not exceed 4096 characters");

        RuleFor(x => x.Provider)
            .NotEmpty().WithMessage("Provider is required")
            .MaximumLength(50).WithMessage("Provider must not exceed 50 characters");
    }
}

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

public class RefreshRequestValidator : AbstractValidator<Controllers.RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("RefreshToken is required")
            .MaximumLength(500).WithMessage("RefreshToken must not exceed 500 characters");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");
    }
}

public class LogoutRequestValidator : AbstractValidator<Controllers.LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("RefreshToken is required")
            .MaximumLength(500).WithMessage("RefreshToken must not exceed 500 characters");
    }
}

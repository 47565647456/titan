using FluentValidation;
using Titan.API.Controllers;

namespace Titan.API.Validators;

public class UpsertPolicyRequestValidator : AbstractValidator<UpsertPolicyRequest>
{
    public UpsertPolicyRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Policy name is required")
            .MaximumLength(100).WithMessage("Policy name must not exceed 100 characters")
            .Matches(@"^[\w\-]+$").WithMessage("Policy name must contain only alphanumeric characters, underscores, or hyphens");

        RuleFor(x => x.Rules)
            .NotEmpty().WithMessage("At least one rule is required");

        RuleForEach(x => x.Rules)
            .NotEmpty().WithMessage("Rule cannot be empty")
            .MaximumLength(100).WithMessage("Rule must not exceed 100 characters")
            // Allow both formats: 
            // 1. "MaxHits:PeriodSeconds:TimeoutSeconds" (e.g., "50:30:120")
            // 2. "count/period" with optional timeout (e.g., "10/1m" or "100/1h timeout:5m")
            .Matches(@"^(\d+:\d+:\d+|\d+/\d+[smhd](\s*timeout:\s*\d+[smhd])?)$")
            .WithMessage("Rule must be in format 'MaxHits:PeriodSeconds:TimeoutSeconds' (e.g., '50:30:120') or 'count/period' (e.g., '10/1m')");
    }
}

public class AddEndpointMappingRequestValidator : AbstractValidator<AddEndpointMappingRequest>
{
    public AddEndpointMappingRequestValidator()
    {
        RuleFor(x => x.Pattern)
            .NotEmpty().WithMessage("Pattern is required")
            .MaximumLength(500).WithMessage("Pattern must not exceed 500 characters");

        RuleFor(x => x.PolicyName)
            .NotEmpty().WithMessage("Policy name is required")
            .MaximumLength(100).WithMessage("Policy name must not exceed 100 characters");
    }
}

public class SetDefaultPolicyRequestValidator : AbstractValidator<SetDefaultPolicyRequest>
{
    public SetDefaultPolicyRequestValidator()
    {
        RuleFor(x => x.PolicyName)
            .NotEmpty().WithMessage("Policy name is required")
            .MaximumLength(100).WithMessage("Policy name must not exceed 100 characters");
    }
}

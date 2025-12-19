using FluentValidation;

namespace Titan.API.Validators;

#region Request DTOs

/// <summary>
/// Request DTO for validating ID parameters in hubs.
/// </summary>
public record IdRequest(string? Id, string ParameterName);

/// <summary>
/// Request DTO for validating name parameters in hubs.
/// </summary>
public record NameRequest(string? Name, string ParameterName, int MaxLength = 200);

/// <summary>
/// Request DTO for validating positive value parameters in hubs.
/// </summary>
public record PositiveValueRequest(long Value, string ParameterName);

#endregion

#region Validators

/// <summary>
/// Validator for ID parameters (e.g., seasonId, baseTypeId).
/// </summary>
public class IdRequestValidator : AbstractValidator<IdRequest>
{
    public const int MaxIdLength = 100;

    public IdRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage(x => $"{x.ParameterName} is required")
            .MaximumLength(MaxIdLength)
            .WithMessage(x => $"{x.ParameterName} exceeds maximum length of {MaxIdLength}");
    }
}

/// <summary>
/// Validator for name parameters.
/// </summary>
public class NameRequestValidator : AbstractValidator<NameRequest>
{
    public NameRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage(x => $"{x.ParameterName} is required")
            .Must((request, name) => name == null || name.Length <= request.MaxLength)
            .WithMessage(x => $"{x.ParameterName} exceeds maximum length of {x.MaxLength}");
    }
}

/// <summary>
/// Validator for positive value parameters.
/// </summary>
public class PositiveValueRequestValidator : AbstractValidator<PositiveValueRequest>
{
    public PositiveValueRequestValidator()
    {
        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0)
            .WithMessage(x => $"{x.ParameterName} cannot be negative");
    }
}

#endregion

using FluentValidation;
using Microsoft.AspNetCore.SignalR;
using Titan.API.Validators;

namespace Titan.API.Services;

/// <summary>
/// Validation service for SignalR hubs using FluentValidation.
/// Throws HubException with user-friendly messages for invalid input.
/// </summary>
public class HubValidationService
{
    private readonly IValidator<IdRequest> _idValidator;
    private readonly IValidator<NameRequest> _nameValidator;
    private readonly IValidator<NonNegativeValueRequest> _nonNegativeValueValidator;

    public HubValidationService(
        IValidator<IdRequest> idValidator,
        IValidator<NameRequest> nameValidator,
        IValidator<NonNegativeValueRequest> nonNegativeValueValidator)
    {
        _idValidator = idValidator;
        _nameValidator = nameValidator;
        _nonNegativeValueValidator = nonNegativeValueValidator;
    }

    /// <summary>
    /// Validates an ID parameter (seasonId, baseTypeId, etc.)
    /// </summary>
    public void ValidateId(string? id, string parameterName)
    {
        var result = _idValidator.Validate(new IdRequest(id, parameterName));
        if (!result.IsValid)
        {
            throw new HubException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>
    /// Validates a name parameter.
    /// </summary>
    public void ValidateName(string? name, string parameterName, int maxLength = 200)
    {
        var result = _nameValidator.Validate(new NameRequest(name, parameterName, maxLength));
        if (!result.IsValid)
        {
            throw new HubException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>
    /// Validates a non-negative value (>= 0).
    /// </summary>
    public void ValidateNonNegative(long value, string parameterName)
    {
        var result = _nonNegativeValueValidator.Validate(new NonNegativeValueRequest(value, parameterName));
        if (!result.IsValid)
        {
            throw new HubException(result.Errors[0].ErrorMessage);
        }
    }

    // Async versions for backward compatibility with existing hub code
    
    /// <summary>
    /// Validates an ID parameter (seasonId, baseTypeId, etc.)
    /// </summary>
    public Task ValidateIdAsync(string? id, string parameterName)
    {
        ValidateId(id, parameterName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates a name parameter.
    /// </summary>
    public Task ValidateNameAsync(string? name, string parameterName, int maxLength = 200)
    {
        ValidateName(name, parameterName, maxLength);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates a non-negative value (>= 0).
    /// </summary>
    public Task ValidatePositiveAsync(long value, string parameterName)
    {
        ValidateNonNegative(value, parameterName);
        return Task.CompletedTask;
    }
}

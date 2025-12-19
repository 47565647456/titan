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
    private readonly IValidator<PositiveValueRequest> _positiveValueValidator;

    public HubValidationService(
        IValidator<IdRequest> idValidator,
        IValidator<NameRequest> nameValidator,
        IValidator<PositiveValueRequest> positiveValueValidator)
    {
        _idValidator = idValidator;
        _nameValidator = nameValidator;
        _positiveValueValidator = positiveValueValidator;
    }

    /// <summary>
    /// Validates an ID parameter (seasonId, baseTypeId, etc.)
    /// </summary>
    public async Task ValidateIdAsync(string? id, string parameterName)
    {
        var result = await _idValidator.ValidateAsync(new IdRequest(id, parameterName));
        if (!result.IsValid)
        {
            throw new HubException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>
    /// Validates a name parameter.
    /// </summary>
    public async Task ValidateNameAsync(string? name, string parameterName, int maxLength = 200)
    {
        var result = await _nameValidator.ValidateAsync(new NameRequest(name, parameterName, maxLength));
        if (!result.IsValid)
        {
            throw new HubException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>
    /// Validates a positive integer value.
    /// </summary>
    public async Task ValidatePositiveAsync(long value, string parameterName)
    {
        var result = await _positiveValueValidator.ValidateAsync(new PositiveValueRequest(value, parameterName));
        if (!result.IsValid)
        {
            throw new HubException(result.Errors[0].ErrorMessage);
        }
    }
}

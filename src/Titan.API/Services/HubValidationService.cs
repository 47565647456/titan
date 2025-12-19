using FluentValidation;
using Microsoft.AspNetCore.SignalR;

namespace Titan.API.Services;

/// <summary>
/// Validation service for SignalR hubs using FluentValidation.
/// Wraps FluentValidation to throw HubException with user-friendly messages.
/// </summary>
public class HubValidationService
{
    private readonly IServiceProvider _serviceProvider;

    public HubValidationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Validates a request object using its registered FluentValidation validator.
    /// Throws HubException if validation fails.
    /// </summary>
    /// <typeparam name="T">The type of request to validate.</typeparam>
    /// <param name="request">The request object to validate.</param>
    /// <exception cref="HubException">Thrown when validation fails.</exception>
    public async Task ValidateAndThrowAsync<T>(T request)
    {
        var validator = _serviceProvider.GetService<IValidator<T>>();
        if (validator == null)
        {
            // No validator registered - allow the request through
            return;
        }

        var result = await validator.ValidateAsync(request);
        if (!result.IsValid)
        {
            // Return the first error message (most relevant for user)
            var firstError = result.Errors.First();
            throw new HubException(firstError.ErrorMessage);
        }
    }

    /// <summary>
    /// Validates a request object synchronously.
    /// Use ValidateAndThrowAsync when possible.
    /// </summary>
    public void ValidateAndThrow<T>(T request)
    {
        var validator = _serviceProvider.GetService<IValidator<T>>();
        if (validator == null)
        {
            return;
        }

        var result = validator.Validate(request);
        if (!result.IsValid)
        {
            var firstError = result.Errors.First();
            throw new HubException(firstError.ErrorMessage);
        }
    }
}

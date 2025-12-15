namespace Titan.Abstractions.Rules;

public interface IRule<in TContext>
{
    /// <summary>
    /// Validates the given context.
    /// Throws an exception if validation fails.
    /// </summary>
    /// <param name="context">The context to validate.</param>
    Task ValidateAsync(TContext context);
}

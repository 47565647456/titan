using Titan.Abstractions.Models;
using Titan.Abstractions.Rules;

namespace Titan.Grains.Trading.Rules;

public record TradeRequestContext(Character Initiator, Character Target);

public class SameSeasonRule : IRule<TradeRequestContext>
{
    public Task ValidateAsync(TradeRequestContext context)
    {
        if (context.Initiator.SeasonId != context.Target.SeasonId)
            throw new InvalidOperationException("Cannot trade across seasons.");
        return Task.CompletedTask;
    }
}

public class SoloSelfFoundRule : IRule<TradeRequestContext>
{
    public Task ValidateAsync(TradeRequestContext context)
    {
        if (context.Initiator.Restrictions.HasFlag(CharacterRestrictions.SoloSelfFound))
            throw new InvalidOperationException("Trading is disabled for Solo Self-Found characters.");

        if (context.Target.Restrictions.HasFlag(CharacterRestrictions.SoloSelfFound))
            throw new InvalidOperationException("Cannot trade with a Solo Self-Found character.");

        return Task.CompletedTask;
    }
}

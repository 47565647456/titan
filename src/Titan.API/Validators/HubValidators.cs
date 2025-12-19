using FluentValidation;
using Titan.API.Hubs;

namespace Titan.API.Validators;

/// <summary>
/// FluentValidation validators for SignalR hub request DTOs.
/// These provide consistent validation with HTTP controller validators.
/// </summary>

#region Account Hub Validators

public class CreateCharacterRequestValidator : AbstractValidator<CreateCharacterRequest>
{
    public CreateCharacterRequestValidator()
    {
        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]+$").WithMessage("SeasonId must contain only alphanumeric characters, underscores, hyphens, or periods");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(50).WithMessage("Name must not exceed 50 characters");
    }
}

public class IdRequestValidator : AbstractValidator<IdRequest>
{
    public IdRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required")
            .MaximumLength(100).WithMessage("Id must not exceed 100 characters");
    }
}

#endregion

#region Character Hub Validators

public class CharacterSeasonRequestValidator : AbstractValidator<CharacterSeasonRequest>
{
    public CharacterSeasonRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");
    }
}

public class AddExperienceRequestValidator : AbstractValidator<AddExperienceRequest>
{
    public AddExperienceRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative");
    }
}

public class SetStatRequestValidator : AbstractValidator<SetStatRequest>
{
    public SetStatRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.StatName)
            .NotEmpty().WithMessage("StatName is required")
            .MaximumLength(100).WithMessage("StatName must not exceed 100 characters");
    }
}

public class UpdateChallengeRequestValidator : AbstractValidator<UpdateChallengeRequest>
{
    public UpdateChallengeRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithMessage("ChallengeId is required")
            .MaximumLength(100).WithMessage("ChallengeId must not exceed 100 characters");

        RuleFor(x => x.Progress)
            .GreaterThanOrEqualTo(0).WithMessage("Progress cannot be negative");
    }
}

#endregion

#region Season Hub Validators

public class CreateSeasonHubRequestValidator : AbstractValidator<CreateSeasonHubRequest>
{
    public CreateSeasonHubRequestValidator()
    {
        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]+$").WithMessage("SeasonId must contain only alphanumeric characters, underscores, hyphens, or periods");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters");

        RuleFor(x => x.MigrationTargetId)
            .MaximumLength(100).WithMessage("MigrationTargetId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]*$").WithMessage("MigrationTargetId must contain only alphanumeric characters, underscores, hyphens, or periods")
            .When(x => !string.IsNullOrEmpty(x.MigrationTargetId));

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("StartDate is required");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("EndDate must be after StartDate")
            .When(x => x.EndDate.HasValue);
    }
}

public class StartMigrationRequestValidator : AbstractValidator<StartMigrationRequest>
{
    public StartMigrationRequestValidator()
    {
        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.TargetSeasonId)
            .MaximumLength(100).WithMessage("TargetSeasonId must not exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.TargetSeasonId));
    }
}

#endregion

#region Inventory Hub Validators

public class MoveBagItemRequestValidator : AbstractValidator<MoveBagItemRequest>
{
    public MoveBagItemRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("ItemId is required");

        RuleFor(x => x.NewX)
            .GreaterThanOrEqualTo(0).WithMessage("NewX cannot be negative");

        RuleFor(x => x.NewY)
            .GreaterThanOrEqualTo(0).WithMessage("NewY cannot be negative");
    }
}

public class EquipRequestValidator : AbstractValidator<EquipRequest>
{
    public EquipRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.BagItemId)
            .NotEmpty().WithMessage("BagItemId is required");

        RuleFor(x => x.Slot)
            .IsInEnum().WithMessage("Invalid equipment slot");
    }
}

public class UnequipRequestValidator : AbstractValidator<UnequipRequest>
{
    public UnequipRequestValidator()
    {
        RuleFor(x => x.CharacterId)
            .NotEmpty().WithMessage("CharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");

        RuleFor(x => x.Slot)
            .IsInEnum().WithMessage("Invalid equipment slot");
    }
}

#endregion

#region Trade Hub Validators

public class StartTradeRequestValidator : AbstractValidator<StartTradeRequest>
{
    public StartTradeRequestValidator()
    {
        RuleFor(x => x.MyCharacterId)
            .NotEmpty().WithMessage("MyCharacterId is required");

        RuleFor(x => x.TargetCharacterId)
            .NotEmpty().WithMessage("TargetCharacterId is required");

        RuleFor(x => x.SeasonId)
            .NotEmpty().WithMessage("SeasonId is required")
            .MaximumLength(100).WithMessage("SeasonId must not exceed 100 characters");
    }
}

public class TradeItemRequestValidator : AbstractValidator<TradeItemRequest>
{
    public TradeItemRequestValidator()
    {
        RuleFor(x => x.TradeId)
            .NotEmpty().WithMessage("TradeId is required");

        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("ItemId is required");
    }
}

#endregion

#region BaseType Hub Validators

/// <summary>
/// Validator for BaseType model used in SignalR hub Create/Update operations.
/// Ensures all required fields are present and within valid ranges.
/// </summary>
public class BaseTypeModelValidator : AbstractValidator<Titan.Abstractions.Models.Items.BaseType>
{
    public BaseTypeModelValidator()
    {
        RuleFor(x => x.BaseTypeId)
            .NotEmpty().WithMessage("BaseTypeId is required")
            .MaximumLength(100).WithMessage("BaseTypeId must not exceed 100 characters")
            .Matches(@"^[\w\-\.]+$").WithMessage("BaseTypeId must contain only alphanumeric characters, underscores, hyphens, or periods");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters");

        RuleFor(x => x.Width)
            .InclusiveBetween(1, 100).WithMessage("Width must be between 1 and 100");

        RuleFor(x => x.Height)
            .InclusiveBetween(1, 100).WithMessage("Height must be between 1 and 100");

        RuleFor(x => x.MaxStackSize)
            .InclusiveBetween(1, 9999).WithMessage("MaxStackSize must be between 1 and 9999");
    }
}

#endregion

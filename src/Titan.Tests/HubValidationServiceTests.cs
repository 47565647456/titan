using FluentValidation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Titan.API.Hubs;
using Titan.API.Services;

namespace Titan.Tests;

/// <summary>
/// Unit tests for HubValidationService.
/// </summary>
public class HubValidationServiceTests
{
    private readonly HubValidationService _service;
    private readonly IServiceProvider _serviceProvider;

    public HubValidationServiceTests()
    {
        // Create service collection with validators
        var services = new ServiceCollection();
        services.AddValidatorsFromAssemblyContaining<Titan.API.Validators.CreateCharacterRequestValidator>();
        _serviceProvider = services.BuildServiceProvider();
        _service = new HubValidationService(_serviceProvider);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_ValidRequest_DoesNotThrow()
    {
        // Arrange
        var request = new CreateCharacterRequest("season-1", "TestCharacter", Titan.Abstractions.Models.CharacterRestrictions.None);

        // Act & Assert - should not throw
        await _service.ValidateAndThrowAsync(request);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_InvalidSeasonId_ThrowsHubException()
    {
        // Arrange - empty seasonId is invalid
        var request = new CreateCharacterRequest("", "TestCharacter", Titan.Abstractions.Models.CharacterRestrictions.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _service.ValidateAndThrowAsync(request));
        Assert.Contains("SeasonId", exception.Message);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_InvalidName_ThrowsHubException()
    {
        // Arrange - empty name is invalid
        var request = new CreateCharacterRequest("season-1", "", Titan.Abstractions.Models.CharacterRestrictions.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _service.ValidateAndThrowAsync(request));
        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_NameTooLong_ThrowsHubException()
    {
        // Arrange - name longer than 50 chars
        var longName = new string('a', 51);
        var request = new CreateCharacterRequest("season-1", longName, Titan.Abstractions.Models.CharacterRestrictions.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _service.ValidateAndThrowAsync(request));
        Assert.Contains("50", exception.Message);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_InvalidSeasonIdFormat_ThrowsHubException()
    {
        // Arrange - seasonId with invalid characters
        var request = new CreateCharacterRequest("season/invalid", "TestCharacter", Titan.Abstractions.Models.CharacterRestrictions.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _service.ValidateAndThrowAsync(request));
        Assert.Contains("alphanumeric", exception.Message);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_ValidIdRequest_DoesNotThrow()
    {
        // Arrange
        var request = new IdRequest("cosmetic-123");

        // Act & Assert - should not throw
        await _service.ValidateAndThrowAsync(request);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_EmptyIdRequest_ThrowsHubException()
    {
        // Arrange
        var request = new IdRequest("");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _service.ValidateAndThrowAsync(request));
        Assert.Contains("Id is required", exception.Message);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_AddExperienceNegative_ThrowsHubException()
    {
        // Arrange - negative experience amount
        var request = new AddExperienceRequest(Guid.NewGuid(), "season-1", -100);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HubException>(() => _service.ValidateAndThrowAsync(request));
        Assert.Contains("negative", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAndThrowAsync_ValidAddExperience_DoesNotThrow()
    {
        // Arrange
        var request = new AddExperienceRequest(Guid.NewGuid(), "season-1", 1000);

        // Act & Assert - should not throw
        await _service.ValidateAndThrowAsync(request);
    }
}

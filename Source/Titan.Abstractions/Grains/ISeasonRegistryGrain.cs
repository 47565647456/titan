using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Singleton grain managing all seasons.
/// Key: "default"
/// </summary>
public interface ISeasonRegistryGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets the currently active temporary season (if any).
    /// </summary>
    Task<Season?> GetCurrentSeasonAsync();

    /// <summary>
    /// Gets a specific season by ID.
    /// </summary>
    Task<Season?> GetSeasonAsync(string seasonId);

    /// <summary>
    /// Gets all seasons (permanent and temporary).
    /// </summary>
    Task<IReadOnlyList<Season>> GetAllSeasonsAsync();

    /// <summary>
    /// Creates a new season (Admin only).
    /// </summary>
    Task<Season> CreateSeasonAsync(Season season);

    /// <summary>
    /// Ends a season and triggers migration (Admin only).
    /// </summary>
    Task<Season> EndSeasonAsync(string seasonId);

    /// <summary>
    /// Updates a season's status.
    /// </summary>
    Task<Season> UpdateSeasonStatusAsync(string seasonId, SeasonStatus status);
}

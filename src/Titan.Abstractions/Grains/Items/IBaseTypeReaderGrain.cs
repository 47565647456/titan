using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Stateless worker for high-performance base type lookups.
/// Implementation should be marked with [StatelessWorker].
/// Key: "default"
/// </summary>
public interface IBaseTypeReaderGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets a base type by ID.
    /// </summary>
    Task<BaseType?> GetAsync(string baseTypeId);

    /// <summary>
    /// Gets base types matching the specified tags.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetByTagsAsync(params string[] tags);

    /// <summary>
    /// Gets the grid dimensions for a base type.
    /// </summary>
    Task<(int Width, int Height)> GetGridSizeAsync(string baseTypeId);

    /// <summary>
    /// Gets the requirements for a base type.
    /// </summary>
    Task<(int Level, int Str, int Dex, int Int)> GetRequirementsAsync(string baseTypeId);
}

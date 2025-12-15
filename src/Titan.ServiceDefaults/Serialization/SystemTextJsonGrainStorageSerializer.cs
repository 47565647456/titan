using System.Text.Json;
using Orleans.Storage;

namespace Titan.ServiceDefaults.Serialization;

/// <summary>
/// Grain storage serializer using System.Text.Json for JSON serialization.
/// Used as a fallback for storage providers that can't use MemoryPack
/// (e.g., TransactionStore which uses Orleans internal types).
/// </summary>
public class SystemTextJsonGrainStorageSerializer : IGrainStorageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        // Include fields for record types
        IncludeFields = true
    };

    /// <inheritdoc/>
    public BinaryData Serialize<T>(T input)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(input, Options);
        return new BinaryData(bytes);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(BinaryData input)
    {
        return JsonSerializer.Deserialize<T>(input.ToArray(), Options)!;
    }
}

using MemoryPack;
using Orleans.Storage;

namespace Titan.ServiceDefaults.Serialization;

/// <summary>
/// Grain storage serializer using MemoryPack for high-performance binary serialization.
/// Replaces the default JSON serialization for smaller payloads and faster read/write.
/// </summary>
public class MemoryPackGrainStorageSerializer : IGrainStorageSerializer
{
    /// <inheritdoc/>
    public BinaryData Serialize<T>(T input)
    {
        var bytes = MemoryPackSerializer.Serialize(input);
        return new BinaryData(bytes);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(BinaryData input)
    {
        return MemoryPackSerializer.Deserialize<T>(input.ToArray())!;
    }
}

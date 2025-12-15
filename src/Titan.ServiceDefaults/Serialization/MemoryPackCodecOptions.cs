namespace Titan.ServiceDefaults.Serialization;

/// <summary>
/// Configuration options for MemoryPack serialization in Orleans.
/// </summary>
public class MemoryPackCodecOptions
{
    /// <summary>
    /// Optional delegate to determine if a type is serializable.
    /// Return true to serialize, false to skip, null to use default logic.
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// Optional delegate to determine if a type is copyable.
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

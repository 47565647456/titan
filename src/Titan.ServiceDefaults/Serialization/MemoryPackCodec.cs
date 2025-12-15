using System.Collections.Concurrent;
using System.Reflection;
using MemoryPack;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Titan.ServiceDefaults.Serialization;

/// <summary>
/// A serialization codec which uses MemoryPack for high-performance binary serialization.
/// Implements IGeneralizedCodec, IGeneralizedCopier, and ITypeFilter for Orleans integration.
/// </summary>
/// <remarks>
/// Based on the Orleans MessagePackCodec pattern. Supports all types decorated with [MemoryPackable].
/// </remarks>
[Alias(WellKnownAlias)]
public class MemoryPackCodec : IGeneralizedCodec, IGeneralizedCopier, ITypeFilter
{
    private static readonly ConcurrentDictionary<Type, bool> SupportedTypes = new();
    private static readonly Type SelfType = typeof(MemoryPackCodec);

    private readonly ICodecSelector[] _serializableTypeSelectors;
    private readonly ICopierSelector[] _copyableTypeSelectors;
    private readonly MemoryPackCodecOptions _options;

    /// <summary>
    /// The well-known type alias for this codec.
    /// </summary>
    public const string WellKnownAlias = "mempack";

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryPackCodec"/> class.
    /// </summary>
    /// <param name="serializableTypeSelectors">Filters used to indicate which types should be serialized by this codec.</param>
    /// <param name="copyableTypeSelectors">Filters used to indicate which types should be copied by this codec.</param>
    /// <param name="options">The MemoryPack codec options.</param>
    public MemoryPackCodec(
        IEnumerable<ICodecSelector> serializableTypeSelectors,
        IEnumerable<ICopierSelector> copyableTypeSelectors,
        IOptions<MemoryPackCodecOptions> options)
    {
        _serializableTypeSelectors = serializableTypeSelectors
            .Where(t => string.Equals(t.CodecName, WellKnownAlias, StringComparison.Ordinal))
            .ToArray();
        _copyableTypeSelectors = copyableTypeSelectors
            .Where(t => string.Equals(t.CopierName, WellKnownAlias, StringComparison.Ordinal))
            .ToArray();
        _options = options.Value;
    }

    /// <inheritdoc/>
    void IFieldCodec.WriteField<TBufferWriter>(
        ref Writer<TBufferWriter> writer,
        uint fieldIdDelta,
        Type expectedType,
        object value)
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }

        // The schema type when serializing the field is the type of the codec.
        writer.WriteFieldHeader(fieldIdDelta, expectedType, SelfType, WireType.TagDelimited);

        // Write the type name
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(0, WireType.LengthPrefixed);
        writer.Session.TypeCodec.WriteLengthPrefixed(ref writer, value.GetType());

        // Serialize with MemoryPack
        var bytes = MemoryPackSerializer.Serialize(value.GetType(), value);

        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(1, WireType.LengthPrefixed);
        writer.WriteVarUInt32((uint)bytes.Length);
        writer.Write(bytes);

        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    object IFieldCodec.ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.IsReference)
        {
            return ReferenceCodec.ReadReference(ref reader, field.FieldType);
        }

        field.EnsureWireTypeTagDelimited();

        var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        object? result = null;
        Type? type = null;
        uint fieldId = 0;

        while (true)
        {
            var header = reader.ReadFieldHeader();
            if (header.IsEndBaseOrEndObject)
            {
                break;
            }

            fieldId += header.FieldIdDelta;
            switch (fieldId)
            {
                case 0:
                    ReferenceCodec.MarkValueField(reader.Session);
                    type = reader.Session.TypeCodec.ReadLengthPrefixed(ref reader);
                    break;
                case 1:
                    if (type is null)
                    {
                        ThrowTypeFieldMissing();
                        return null!; // Unreachable, but helps compiler know we return
                    }

                    ReferenceCodec.MarkValueField(reader.Session);
                    var length = (int)reader.ReadVarUInt32();
                    var bytes = new byte[length];
                    reader.ReadBytes(bytes);
                    result = MemoryPackSerializer.Deserialize(type!, bytes) 
                        ?? throw new InvalidOperationException($"MemoryPack deserialization returned null for type {type}");
                    break;
                default:
                    reader.ConsumeUnknownField(header);
                    break;
            }
        }

        ReferenceCodec.RecordObject(reader.Session, result!, placeholderReferenceId);
        return result!;
    }

    /// <inheritdoc/>
    bool IGeneralizedCodec.IsSupportedType(Type type)
    {
        if (type == SelfType)
        {
            return true;
        }

        if (CommonCodecTypeFilter.IsAbstractOrFrameworkType(type))
        {
            return false;
        }

        foreach (var selector in _serializableTypeSelectors)
        {
            if (selector.IsSupportedType(type))
            {
                return true;
            }
        }

        if (_options.IsSerializableType?.Invoke(type) is bool value)
        {
            return value;
        }

        return IsMemoryPackable(type);
    }

    /// <inheritdoc/>
    object? IDeepCopier.DeepCopy(object? input, CopyContext context)
    {
        if (input is null)
        {
            return null;
        }

        if (context.TryGetCopy(input, out object? result))
        {
            return result!;
        }

        // Roundtrip through MemoryPack for deep copy
        var bytes = MemoryPackSerializer.Serialize(input.GetType(), input);
        result = MemoryPackSerializer.Deserialize(input.GetType(), bytes)!;

        context.RecordCopy(input, result);
        return result;
    }

    /// <inheritdoc/>
    bool IGeneralizedCopier.IsSupportedType(Type type)
    {
        if (CommonCodecTypeFilter.IsAbstractOrFrameworkType(type))
        {
            return false;
        }

        foreach (var selector in _copyableTypeSelectors)
        {
            if (selector.IsSupportedType(type))
            {
                return true;
            }
        }

        if (_options.IsCopyableType?.Invoke(type) is bool value)
        {
            return value;
        }

        return IsMemoryPackable(type);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns true for Exception types to allow Orleans' built-in ExceptionCodec to serialize them.
    /// This prevents CodecNotFoundException when MemoryPack throws MemoryPackSerializationException.
    /// </remarks>
    bool? ITypeFilter.IsTypeAllowed(Type type)
    {
        // Allow Exception types so Orleans' ExceptionCodec can serialize them
        // This is critical for error propagation when MemoryPack throws exceptions
        if (typeof(Exception).IsAssignableFrom(type))
        {
            return true;
        }
        
        return (((IGeneralizedCopier)this).IsSupportedType(type) ||
                ((IGeneralizedCodec)this).IsSupportedType(type)) ? true : null;
    }

    private static bool IsMemoryPackable(Type type)
    {
        if (SupportedTypes.TryGetValue(type, out bool isMemoryPackable))
        {
            return isMemoryPackable;
        }

        // Check for [MemoryPackable] attribute
        isMemoryPackable = type.GetCustomAttribute<MemoryPackableAttribute>() is not null;
        SupportedTypes.TryAdd(type, isMemoryPackable);
        return isMemoryPackable;
    }

    private static void ThrowTypeFieldMissing() =>
        throw new InvalidOperationException("Serialized value is missing its type field.");
}

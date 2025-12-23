using MemoryPack;

namespace Titan.Abstractions.Models;

/// <summary>
/// Represents an invocation request that is sent inside a SecureEnvelope.
/// </summary>
[MemoryPackable]
public partial record EncryptedInvocation(
    string Target,
    byte[] Payload);

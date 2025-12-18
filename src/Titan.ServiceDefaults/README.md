# Titan.ServiceDefaults

A shared library containing standard Aspire service configurations and extension methods. This project ensures consistent observability, health checks, and logging across all services.

## Features

### OpenTelemetry
Automatically configures metrics and tracing.
- **Metrics**: ASP.NET Core, HttpClient, Runtime, `Microsoft.Orleans`, and `Titan.RateLimiting`.
- **Tracing**: ASP.NET Core, HttpClient, and `Microsoft.Orleans.Runtime`.
- **Exporters**: OTLP (for Aspire Dashboard).

### Health Checks
- Adds `/health` (detailed JSON) and `/alive` (simple status) endpoints.
- `/health`: Returns per-service breakdown of health (DB, Redis, Silo) with duration.
- Maps default probes for Kubernetes/Container orchestration.

### Logging
- **Serilog**: Standardized console logging.
- **File Logging**: Enabled in Development (daily rolling files).
- **Sentry**: Configured via `AddTitanLogging`. Captures Errors and Exceptions if `Sentry:Dsn` is present.

### Configuration Validation
- `ValidateTitanConfiguration`: A helper to fail-fast on startup if critical config (like JWT Keys or Database passwords) is missing.

### Grain Storage
- `AddTitanGrainStorage(IConfiguration)`: Configures Orleans grain persistence backed by PostgreSQL.
  - **OrleansStorage**: Default grain storage for most grains.
  - **TransactionStore**: For Orleans transaction state.
  - **GlobalStorage**: Shared state (seasons, trades, migrations).

Silo hosts use this extension:
```csharp
silo.AddTitanGrainStorage(builder.Configuration);
```

### Serialization
Titan uses high-performance serialization throughout:

#### Wire Serialization (Orleans RPC)
- **MemoryPackCodec**: Custom Orleans codec for grain-to-grain communication
- Types decorated with `[MemoryPackable]` are automatically serialized using MemoryPack
- Exception handling configured via `ExceptionSerializationOptions`

#### Storage Serialization (Grain Persistence)
| Provider | Serializer | Purpose |
|----------|------------|---------|
| `OrleansStorage` | MemoryPack | Application grain state |
| `GlobalStorage` | MemoryPack | Global singleton grains |
| `PubSubStore` | MemoryPack | Stream pub/sub state |
| `TransactionStore` | System.Text.Json | Orleans transaction internals |

**Factory methods:**
```csharp
// For application grain storage (faster, ~40% smaller payloads)
options.GrainStorageSerializer = MemoryPackSerializerExtensions.CreateMemoryPackGrainStorageSerializer();

// For transaction storage (Orleans internal compatibility)
options.GrainStorageSerializer = MemoryPackSerializerExtensions.CreateSystemTextJsonGrainStorageSerializer();
```

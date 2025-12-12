# Titan.ServiceDefaults

A shared library containing standard Aspire service configurations and extension methods. This project ensures consistent observability, health checks, and logging across all services.

## Features

### OpenTelemetry
Automatically configures metrics and tracing.
- **Metrics**: ASP.NET Core, HttpClient, Runtime.
- **Tracing**: ASP.NET Core, HttpClient.
- **Exporters**: OTLP (for Aspire Dashboard).

### Health Checks
- Adds `/health` and `/alive` endpoints.
- Maps default probes for Kubernetes/Container orchestration.

### Logging
- **Serilog**: Standardized console logging.
- **File Logging**: Enabled in Development (daily rolling files).
- **Sentry**: Configured via `AddTitanLogging`. Captures Errors and Exceptions if `Sentry:Dsn` is present.

### Configuration Validation
- `ValidateTitanConfiguration`: A helper to fail-fast on startup if critical config (like JWT Keys or Database passwords) is missing.

### Grain Storage
- `AddTitanGrainStorage(IConfiguration)`: Configures Orleans grain persistence.
  - **OrleansStorage**: Default grain storage for most grains.
  - **TransactionStore**: For Orleans transaction state.
  - **GlobalStorage**: Shared state (seasons, trades, migrations).
- **RetryingGrainStorage**: A wrapper that adds exponential backoff retry logic to handle transient database failures.

Silo hosts use this extension:
```csharp
silo.AddTitanGrainStorage(builder.Configuration);
```

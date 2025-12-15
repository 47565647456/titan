# Titan.LoadTests

NBomber-based load testing for Titan distributed game backend.

## Prerequisites

- .NET 10 SDK
- Running Titan.AppHost (start with `dotnet run --project Titan.AppHost --launch-profile Development`)

## Disabling Rate Limiting

For accurate stress testing, disable rate limiting using one of these methods:

**Option 1: Use the LoadTest launch profile (recommended)**
```powershell
dotnet run --project Titan.AppHost --launch-profile LoadTest
```

**Option 2: Environment variable**
```powershell
$env:RateLimiting__Enabled = "false"
dotnet run --project Titan.AppHost
```

**Option 3: appsettings override**
Add to `Titan.API/appsettings.Development.json`:
```json
{
  "RateLimiting": {
    "Enabled": false
  }
}
```

## Quick Start

```powershell
# Navigate to src directory
cd "c:\Users\Dan\Documents\Unreal Projects\Titan\src"

# Run quick smoke test (10 users, 30 seconds, all scenarios)
dotnet run --project Titan.LoadTests --launch-profile Development

# Specify custom parameters
dotnet run --project Titan.LoadTests -- --url https://localhost:7001 --users 50 --duration 120

# Run specific scenario
dotnet run --project Titan.LoadTests -- --scenario auth --users 100 --duration 60
```

## Scenarios

| Scenario | Description | Key Metric |
|----------|-------------|------------|
| `auth` | HTTP login throughput | Logins/second |
| `character` | Character creation + inventory read | Operations/second |
| `trading` | Full trade flow (2 users per trade) | Trades/second |
| `all` | Runs all scenarios concurrently | Combined throughput |

## CLI Options

| Option | Default | Description |
|--------|---------|-------------|
| `--url` | `https://localhost:7001` | API base URL |
| `--scenario` | `all` | Scenario to run |
| `--users` | `10` | Concurrent users or requests/sec |
| `--duration` | `30` | Test duration in seconds |

## Output

Reports are saved to `Titan.LoadTests/reports/` in both HTML and Markdown formats.

### Key Metrics in Report

- **RPS** - Requests per second (throughput)
- **Latency p50/p75/p95/p99** - Response time percentiles
- **Success Rate** - Percentage of successful requests
- **Data Transfer** - Bytes sent/received

## Example Load Profiles

```powershell
# Light load - development testing
dotnet run --project Titan.LoadTests -- --users 5 --duration 30

# Medium load - staging verification
dotnet run --project Titan.LoadTests -- --users 50 --duration 120

# Heavy load - stress testing
dotnet run --project Titan.LoadTests -- --users 200 --duration 300

# Auth-focused test
dotnet run --project Titan.LoadTests -- --scenario auth --users 100 --duration 60
```

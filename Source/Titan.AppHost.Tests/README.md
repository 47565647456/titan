# Titan.AppHost.Tests

## Overview
Integration tests for the full Aspire-orchestrated environment using `DistributedApplicationTestingBuilder`.

## Test Categories

| File | Tests | Description |
|------|-------|-------------|
| AuthenticationTests | 4 | JWT generation, role claims, connection authorization |
| AccountTests | 4 | Account CRUD, character creation, IDOR prevention, onboarding flow |
| AdminTests | 4 | Admin role enforcement for ItemType and Season operations |
| TradingTests | 3 | Trade flow, atomic transfers, ownership verification |
| ResourceTests | 2 | API health checks, Orleans cluster health |

## Running Tests

```bash
# Requires Docker (for PostgreSQL, Redis)
dotnet test Titan.AppHost.Tests
```

## Notes
- Tests start the full AppHost with all Orleans silos, Redis, and PostgreSQL
- Each test class inherits from `IntegrationTestBase` which handles startup/teardown
- Use `LoginAsUserAsync()` and `LoginAsAdminAsync()` helpers for authentication



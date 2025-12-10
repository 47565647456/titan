# Titan.Tests

## Overview
**Titan.Tests** contains the **Orleans Integration Tests** for the Titan backend. Unlike `Titan.AppHost.Tests` which runs the full external stack via Aspire, these tests run against an **In-Memory Orleans Test Cluster**.

## Role in Global Solution
This project provides fast feedback for grain logic and interactions. It uses `Microsoft.Orleans.TestingHost` to spin up a lightweight, in-process cluster. This allows for testing grain logic, state machines, and streams without the overhead of Docker containers or a full HTTP API layer.

## Key Responsibilities
- **Grain Logic Verification**: Tests specific grain behaviors (e.g., `TradeGrain` state machine transitions, `InventoryGrain` stacking rules).
- **Cluster Simulation**: Uses `TestCluster` to simulate distributed grain interactions in memory.
- **Fast Execution**: Designed to run quickly during development.

## Comparison with Titan.AppHost.Tests
| Feature | Titan.Tests | Titan.AppHost.Tests |
|---------|-------------|---------------------|
| **Scope** | Grain & Logic | Full System (API + Silos + DB) |
| **Runtime** | In-Process TestCluster | Docker / Aspire AppHost |
| **Speed** | Fast | Slow (Container startup) |
| **Use Case** | TDD, Logic Verification | End-to-End flows, Configuration checks |

## Usage
```bash
dotnet test Titan.Tests
```

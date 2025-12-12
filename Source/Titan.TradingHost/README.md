# Titan.TradingHost

A dedicated Orleans Silo responsible for hosting Trade-related grains.

## Roles
1. **Grain Host**: Hosts `TradeGrain`.
2. **Matching**: Handles trade expiration checks via grain timers.

## Configuration
- **Trading Options**: Configured via `TradingOptions`.
  - `TradeTimeout`: Duration (default 15m).
  - `ExpirationCheckInterval`: Frequency of cleanup jobs.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Critical for trading; uses Orleans Transactions to ensure item swaps are atomic (ACID).

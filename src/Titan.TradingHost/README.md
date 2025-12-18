# Titan.TradingHost

A dedicated Orleans Silo responsible for hosting Trade-related grains.

## Roles
1. **Trade Orchestration**: Primary silo for `TradeGrain`.
2. **Transaction Management**: Manages cross-silo transactions for secure item swaps.
3. **Event Streaming**: Publishes real-time trade updates via `MemoryStreams`.

## Configuration
- **Trading Options**: Configured via `TradingOptions`.
  - `TradeTimeout`: Duration before a pending trade expires.
  - `ExpirationCheckInterval`: Frequency of cleanup jobs.
- **Rule Enforcement**: Registers `IRule<TradeRequestContext>` (e.g., `SameSeasonRule`, `SoloSelfFoundRule`) to validate trades before they start.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Critical for trading; uses Orleans Transactions to ensure item swaps are atomic (ACID).

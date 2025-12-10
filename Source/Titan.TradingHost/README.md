# Titan.TradingHost

## Overview
**Titan.TradingHost** is an Orleans Silo executable specialized for the Trading domain.

## Role in Global Solution
Trading actions utilize complex state machines and real-time streams to coordinate between multiple players. This host runs the grains responsible for these interactions, ensuring that potential performance spikes during high-trading volumes do not impact core gameplay or inventory management.

## Key Responsibilities
- **Exclusive Grain Hosting**: This is the **only** host that registers `TradingOptions`, making it the exclusive home for `TradeGrain` activations.
- **Stream Processing**: Manages the publication and subscription of trade-related events via Orleans Streams.
- **Cluster Participation**: Joins the distributed Orleans cluster.

## Configuration
The host uses `appsettings.json` for configuration.

### Key Settings
| Section | Setting | Description | Environment Variable Override |
|---------|---------|-------------|------------------------------|
| `Logging` | `FilePath` | Path to the log file. | `Logging__FilePath` |
| `Trading` | `TradeTimeout` | Time before a stalled trade is cancelled. | `Trading__TradeTimeout` |

## Technologies
- **Microsoft.Orleans.Server**: Application runtime.
- **Azure/Redis**: Streaming providers.
- **PostgreSQL**: Grain state persistence.

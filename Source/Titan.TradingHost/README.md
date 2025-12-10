# Titan.TradingHost

## Overview
**Titan.TradingHost** is an Orleans Silo executable specialized for the Trading domain.

## Role in Global Solution
Trading actions utilize complex state machines and real-time streams to coordinate between multiple players. This host runs the grains responsible for these interactions, ensuring that potential performance spikes during high-trading volumes do not impact core gameplay or inventory management.

## Key Responsibilities
- **Exclusive Grain Hosting**: This is the **only** host that registers `TradingOptions`, making it the exclusive home for `TradeGrain` activations.
- **Stream Processing**: Manages the publication and subscription of trade-related events via Orleans Streams.
- **Cluster Participation**: Joins the distributed Orleans cluster.

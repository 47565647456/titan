# Titan.Grains

## Overview
**Titan.Grains** is the core business logic library for the Titan backend. It contains the implementation of the Orleans Grains (actors), which represent the fundamental stateful units of the system (e.g., Users, Characters, Inventories, Trade Sessions).

## Role in Global Solution
This project contains the "Brain" of the backend. While `Titan.Abstractions` defines *what* can be done, `Titan.Grains` defines *how* it is done. This library is referenced and loaded by the Silo Host projects (`Titan.IdentityHost`, `Titan.InventoryHost`, etc.) to execute the logic.

## Key Components
- **Grain Implementations**: Classes implementing the `IGrain` interfaces (e.g., `InventoryGrain`, `AccountGrain`).
- **State Management**: Defines the state classes and uses Orleans persistence attributes to automatically save/load state from the database.
- **Domain Logic**: specific rules for item transfer, inventory validation, trade state machines, etc.

## Architecture Note
This is a class library, not an executable. It is designed to be loaded by an Orleans Silo Host.

### Grain Placement Strategy
The Titan architecture uses **Dependency Injection based placement**. Grains are free to be hosted on any Silo that references this library, *provided that Silo registers the required dependencies*.
- **TradeGrain** requires `TradingOptions`, so it only activates on `Titan.TradingHost`.
- **InventoryGrain** requires `ItemRegistryOptions`, so it activates on `Titan.InventoryHost` (and `Titan.IdentityHost`).
- General grains like `AccountGrain` or `CharacterGrain` have no special requirements and can activate on any available Silo, balancing the load across the cluster.

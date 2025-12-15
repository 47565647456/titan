# Titan.InventoryHost

A dedicated Orleans Silo responsible for hosting Inventory and Item-related grains.

## Roles
1. **Grain Host**: Hosts `CharacterInventoryGrain`, `AccountStashGrain`, `ItemGeneratorGrain`, `ItemHistoryGrain`.
2. **Item Logic**: Handles item generation, stacking rules, and history tracking.

## Configuration
- **Item Registry**: Configured via `ItemRegistryOptions`.
  - `AllowUnknownItemTypes`: Should be `false` in Production to enforce strict item definitions.
- **Item History**: Configured via `ItemHistoryOptions`.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Enabled for atomic multi-grain operations.
- **Serialization**: MemoryPack.

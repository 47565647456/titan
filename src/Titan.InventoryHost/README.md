# Titan.InventoryHost

A dedicated Orleans Silo responsible for hosting Inventory and Item-related grains.

## Roles
1. **Inventory Management**: Primary silo for `CharacterInventoryGrain` and `AccountStashGrain`.
2. **Item Lifecycle**: Hosts `IItemGeneratorGrain` for rolling items and `IItemHistoryGrain` for tracking ownership changes.
3. **Stateless Readers**: Hosts reader-facades for BaseTypes and Modifiers with local caching.

## Configuration
- **Item Registry**: Configured via `ItemRegistryOptions`.
  - `AllowUnknownItemTypes`: Enforces strict type checking for new items.
- **Item History**: Configured via `ItemHistoryOptions` (retention and limits).
- **Registry Cache**: Configured via `ItemRegistryCacheOptions` for performance optimization.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Enabled for atomic multi-grain operations.
- **Serialization**: MemoryPack.

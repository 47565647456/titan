# Titan.InventoryHost

A dedicated Orleans Silo responsible for hosting Inventory and Item-related grains.

## Roles
1. **Grain Host**: Hosts `InventoryGrain` and `ItemGrain`.
2. **Validation**: Configured to validate items against the registry.

## Configuration
- **Item Registry**: Configured via `ItemRegistryOptions`.
  - `AllowUnknownItemTypes`: Defaults to `true` in Development to allow easy item creation. Should be `false` in Production to enforce strict item definitions.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Enabled for atomic multi-grain operations.

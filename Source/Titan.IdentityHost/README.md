# Titan.IdentityHost

A dedicated Orleans Silo responsible for hosting Identity, Social, and Meta-Data related grains.

## Roles
1. **Identity & Social**: Hosts `UserIdentityGrain`, `SocialGrain`, `PlayerPresenceGrain`.
2. **Item Metadata**: Hosts `BaseTypeRegistryGrain` and `ModifierRegistryGrain` to provide game data to other grains.
3. **Seeding**: Runs the `BaseTypeSeedStartupTask` on cluster startup to populate the Base Type Registry from JSON.

## Configuration
- **Base Type Seeding**: Configured via `BaseTypeSeedOptions`.
  - `SeedFilePath`: Path to a JSON seed file. If not set, uses the **embedded** `item-seed-data.json` resource.
  - `ForceSeed`: Can be enabled to overwrite registry changes.
- **Item History**: Configured via `ItemHistoryOptions` (retention policies).

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Enabled for atomic multi-grain operations.
- **Serialization**: MemoryPack.

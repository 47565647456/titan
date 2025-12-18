# Titan.IdentityHost

A dedicated Orleans Silo responsible for hosting Identity, Social, and Meta-Data related grains.

## Roles
1. **Identity & Social**: Primary silo for `UserIdentityGrain`, `SocialGrain`, `PlayerPresenceGrain`.
2. **Item Metadata Registry**: Hosts `BaseTypeRegistryGrain`, `ModifierRegistryGrain`, and `UniqueRegistryGrain`.
3. **Cluster Seeding**: Responsible for executing the initial data seeding via `BaseTypeSeedStartupTask` on cluster startup.

## Configuration
- **Base Type Seeding**: Configured via `BaseTypeSeedOptions`.
  - `SeedFilePath`: Path to a JSON seed file. If not set, uses the **embedded** `item-seed-data.json` resource.
  - `ForceSeed`: Re-runs the seeding process even if data already exists (useful for dev).
- **Item History**: Configured via `ItemHistoryOptions`.
- **Registry Cache**: Configured via `ItemRegistryCacheOptions` for the stateless reader grains.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Enabled for atomic multi-grain operations.
- **Serialization**: MemoryPack.

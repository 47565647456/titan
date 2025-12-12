# Titan.IdentityHost

A dedicated Orleans Silo responsible for hosting Identity-related grains.

## Roles
1. **Grain Host**: Hosts `UserIdentityGrain`, `SocialGrain`, and `ItemTypeRegistryGrain`.
2. **Seeding**: Runs the `ItemTypeSeedHostedService` on startup to populate the Item Registry from JSON.

## Configuration
- **Item Registry**: Configured via `ItemRegistryOptions`.
  - `SeedFilePath`: Defaults to `data/item-types.json`.
  - `ForceSeed`: Can be enabled to overwrite registry changes.

## Infrastructure
- **Clustering**: Redis.
- **Persistence**: Configured via `AddTitanGrainStorage()` from ServiceDefaults.
- **Transactions**: Enabled for atomic multi-grain operations.

# Titan.Grains

The core business logic layer of the Titan backend. This library contains the implementation of all Orleans Grains.

## Implemented Grains

### Identity
Core user and social management grains.
- `UserIdentityGrain`: User authentication and identity resolution.
- `AccountGrain`: Main user profile and account data.
- `UserProfileGrain`: Public profile data (display name, avatar).
- `SocialGrain`: Friend lists and social interactions.
- `PlayerPresenceGrain`: Tracking online status.
- `RefreshTokenGrain`: Persistence for auth refresh tokens.
- `SessionLogGrain`: Audit log for user sessions.

### Items & Inventory
Item management and generation system.
- `CharacterInventoryGrain`: Inventory for a specific character.
- `AccountStashGrain`: Shared stash storage for an account.
- `BaseTypeRegistryGrain`: Writes and manages Base Types (Admin).
- `BaseTypeReaderGrain`: Read-optimized caching facade for Base Types (High Throughput).
- `ModifierRegistryGrain`: Writes and manages Modifiers.
- `ModifierReaderGrain`: Read-optimized caching facade for Modifiers.
- `UniqueRegistryGrain`: Rights and manages Unique Items.
- `ItemGeneratorGrain`: Logic for rolling new items (RNG).
- `ItemHistoryGrain`: Audit log for item ownership and changes.

### Seasons & Characters
- `SeasonRegistryGrain`: Management of active/past game seasons.
- `SeasonMigrationGrain`: Logic for migrating characters between seasons.
- `CharacterGrain`: Character persistence and progression.

### Trading
- `TradeGrain`: Manages secure trade sessions between players.
  - Features: Cross-silo atomic transactions, snapshot isolation, timeout enforcement.

## Persistence
This project uses Orleans 'State' and 'TransactionalState' features.
- **Production**: Maps to PostgreSQL (ADO.NET) via `Titan.Abstractions` configuration.
- **Development**: Supports Memory storage fallback (if configured).

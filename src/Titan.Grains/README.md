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

### Trading & Rate Limiting
- **TradeGrain**: Manages secure trade sessions between players.
  - Features: Cross-silo atomic transactions (via `TransactionalState`), snapshot isolation, timeout enforcement.
- **RateLimitConfigGrain**: Persistent singleton for storing dynamic rate limiting configuration.
  - Features: Policy management, endpoint mapping, global enable/disable switch.

## Persistence
This project uses Orleans 'State' and 'TransactionalState' features.
- **State**: Used by most grains via `IPersistentState<T>`.
- **TransactionalState**: Used by `ICharacterInventoryGrain` (within `Titan.Grains/Items`) and `TradeGrain` for ACID compliance during multi-item transfers.
- **Production Storage**: Maps to PostgreSQL (ADO.NET) via `Titan.Abstractions` configuration.
- **Storage Providers**:
  - `GlobalStorage`: Default provider for long-lived state.
  - `TransactionalStorage`: Provider specifically configured for transaction operations.

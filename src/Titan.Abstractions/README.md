# Titan.Abstractions

This project contains the shared contracts, interfaces, and models used throughout the Titan solution. It serves as the common language between the API, Grains, and Client applications.

## Key Components

### Grain Interfaces
Defines the public methods for all Orleans Grains.

#### Core
- `IUserIdentityGrain`: User authentication and identity resolution.
- `IAccountGrain`: Main user account management.
- `IUserProfileGrain`: User profile data (Display Name, Settings).
- `IRefreshTokenGrain`: Management of refresh tokens and rotation.
- `ICharacterGrain`: Character persistence and logic.
- `ITradeGrain`: Trade session logic.
- `ISocialGrain`: Friend lists and social graph.
- `IPlayerPresenceGrain`: Tracking online status of players.
- `ISessionLogGrain`: Tracking session history.
- `IRateLimitConfigGrain`: Singleton for dynamic management of rate limiting policies.

#### Items & Inventory
- `ICharacterInventoryGrain`: Inventory management for a specific character (grid-based).
- `IAccountStashGrain`: Shared stash storage for a user account.
- `IBaseTypeRegistryGrain`: Registry for item base definitions (Base Types).
- `IBaseTypeReaderGrain`: Stateless worker for high-performance base type lookups.
- `IModifierRegistryGrain`: Registry for item modifiers (Affixes).
- `IModifierReaderGrain`: Stateless worker for high-performance modifier lookups.
- `IUniqueRegistryGrain`: Registry for unique item definitions.
- `IItemGeneratorGrain`: Logic for generating new items (RNG, weighting).
- `IItemHistoryGrain`: Tracking item ownership history.

#### Seasonal
- `ISeasonRegistryGrain`: Management of game seasons and active periods.
- `ISeasonMigrationGrain`: Handling migration of characters/items between seasons.

### Models
Shared data transfer objects (records) used in Grain method signatures and API responses. Optimized for serialization via MemoryPack and Orleans.
- **Items**: `Item`, `BaseType`, `ModifierDefinition`, `InventoryGrid`, `StashTab`, `UniqueDefinition`, `CharacterStats`.
- **Trade**: `TradeStatus`, `TradeSession`, `TradeOffer`, `TradeResult`.
- **Character**: `Character`, `CharacterSummary`, `CharacterCreationRequest`.
- **Identity & Account**: `UserIdentity`, `UserProfile`, `AccountSummary`, `RefreshResult`.
- **Rate Limiting**: `RateLimitRule`, `RateLimitPolicy`, `RateLimitingConfiguration`, `EndpointRateLimitConfig`.
- **Season**: `Season`, `SeasonSummary`, `SeasonStatus`.

### Configuration Options
Configuration classes mapped to `appsettings.json` sections.

#### ItemRegistryOptions
- **SectionName**: `"ItemRegistry"`
- `SeedFilePath`: Path to JSON file for seeding item types.
- `ForceSeed`: (Boolean) Force re-seed on startup.
- `AllowUnknownItemTypes`: (Boolean) Dev flag to allow loose typing.

#### BaseTypeSeedOptions
- **SectionName**: `"BaseTypeSeeding"`
- `SeedFilePath`: Path to JSON seed file.
- `ForceReseed`: Force re-seed even if populated.

#### ItemHistoryOptions
- **SectionName**: `"ItemHistory"`
- `MaxEntriesPerItem`: Limit history entries per item.
- `RetentionDays`: Days to keep history.

#### ItemRegistryCacheOptions
- `CacheDuration`: Duration to cache registry lookups (default: 5m).

#### TradingOptions
- **SectionName**: `"Trading"`
- `TradeTimeout`: Duration before a pending trade expires.
- `MaxItemsPerUser`: Cap on items per trade side.

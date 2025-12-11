# Titan.Abstractions

This project contains the shared contracts, interfaces, and models used throughout the Titan solution. It serves as the common language between the API, Grains, and Client applications.

## Key Components

### Grain Interfaces
Defines the public API for all Orleans Grains.
- `IUserIdentityGrain`: User authentication and identity management.
- `IInventoryGrain`: Item storage and management.
- `ITradeGrain`: Trade session logic.
- `ISocialGrain`: Friend lists and social graph.
- `IItemTypeRegistryGrain`: Metadata for game items.

### Models
Shared data transfer objects (records) used in Grain method signatures.
- `inventory/*`: Item items, stacks, and transfer models.
- `trading/*`: Trade status, offers, and session models.

### Configuration Options
Configuration classes mapped to `appsettings.json` sections.

#### ItemRegistryOptions
Configuration for the Item Type Registry.
- **SectionName**: `"ItemRegistry"`
- `SeedFilePath`: Path to JSON file for seeding item types.
- `AllowUnknownItemTypes`: (Boolean) If true, allows items with unknown Types to be created (useful for dev).

#### TradingOptions
Configuration for the Trading System.
- **SectionName**: `"Trading"`
- `TradeTimeout`: Duration before a pending trade expires (default: 15m).
- `ExpirationCheckInterval`: How often to check for expired trades.
- `MaxItemsPerUser`: Cap on items per trade.

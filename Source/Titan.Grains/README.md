# Titan.Grains

The core business logic layer of the Titan backend. This library contains the implementation of all Orleans Grains.

## Implemented Grains

### Identity
- `UserIdentityGrain`: manages user accounts, linked providers (Steam, EOS, etc.), and profile data.
- **Persistence**: ADO.NET (Postgres).

### Inventory
- `InventoryGrain`: Manages a player's items. Handles stacking, splitting, and limits.
- `ItemTypeRegistryGrain`: Caches item metadata (name, max stack size) for validation.
- **Rules**: Enforces stack limits defined in `ItemTypeRegistry`.

### Trading
- `TradeGrain`: Manages a secure trade session between two players.
- `TradeSession`: State machine for the trade lifecycle (Open -> Locked -> Accepted -> Completed).
- **Features**: 
  - Cross-silo atomic transactions.
  - Snapshot isolation using TransactionalState.
  - Automatic expiration via `RegisterTimer`.

### Social
- `SocialGrain`: Manages friend lists and block lists.

## Persistence
This project uses Orleans 'State' and 'TransactionalState' features.
- In **Production/Aspire**, it maps to the registered ADO.NET providers in the Host projects.
- In **Development/Tests**, it can fallback to Memory storage.

# Titan.Client

Reusable SignalR client SDK for connecting to Titan's API gateway.

## Features

- **Connection Pooling**: Lazily creates and caches hub connections per session
- **Auto-Reconnect**: Built-in automatic reconnection via SignalR's `WithAutomaticReconnect()`
- **Token Management**: Support for updating JWT tokens
- **Connection Events**: Subscribe to `OnConnected`, `OnDisconnected`, `OnReconnecting`, `OnReconnected`
- **Logging**: Optional `ILogger<TitanSession>` integration

## Usage

```csharp
// Create a session after authentication
var session = new TitanSession(apiBaseUrl, jwtToken, userId);

// Subscribe to events (optional)
session.OnDisconnected += (hub, ex) => Console.WriteLine($"Disconnected from {hub}");

// Get hub connections (lazily created)
var accountHub = await session.GetAccountHubAsync();
var account = await accountHub.InvokeAsync<Account>("GetAccount");

// Reuse connections
var characterHub = await session.GetCharacterHubAsync();
var characters = await accountHub.InvokeAsync<IReadOnlyList<CharacterSummary>>("GetCharacters");

// Dispose when done
await session.DisposeAsync();
```

## Hub Accessors

| Method | Hub Path |
|--------|----------|
| `GetAccountHubAsync()` | `/accountHub` |
| `GetAuthHubAsync()` | `/authHub` |
| `GetCharacterHubAsync()` | `/characterHub` |
| `GetInventoryHubAsync()` | `/inventoryHub` |
| `GetTradeHubAsync()` | `/tradeHub` |
| `GetItemTypeHubAsync()` | `/itemTypeHub` |
| `GetSeasonHubAsync()` | `/seasonHub` |

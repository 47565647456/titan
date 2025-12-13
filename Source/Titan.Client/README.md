# Titan.Client

Strongly-typed client SDK for connecting to Titan's API gateway. Provides compile-time safe access to all hub operations using source-generated proxies.

## Features

- **Strongly-Typed API**: All hub methods have compile-time type checking - no more magic strings
- **HTTP Authentication**: Login/logout via industry-standard HTTP endpoints
- **WebSocket Real-Time**: SignalR hubs for real-time operations
- **Auto-Reconnect**: Built-in automatic reconnection via SignalR's `WithAutomaticReconnect()`
- **Fluent Builder**: Easy configuration with `TitanClientBuilder`
- **DI Support**: `AddTitanClient()` extension for dependency injection
- **Logging**: Optional `ILoggerFactory` integration

## Installation

```bash
dotnet add package Titan.Client
```

## Quick Start

```csharp
// Create client with builder
var client = new TitanClientBuilder()
    .WithBaseUrl("https://api.titan.gg")
    .WithLogging(loggerFactory)
    .Build();

// Authenticate via HTTP (industry standard)
var loginResult = await client.Auth.LoginAsync("provider-token", "EOS");
Console.WriteLine($"Logged in as: {loginResult.UserId}");

// Use typed hub clients (compile-time safe!)
var accountClient = await client.GetAccountClientAsync();
var account = await accountClient.GetAccount();
Console.WriteLine($"Unlocked cosmetics: {account.UnlockedCosmetics.Count}");

var characterClient = await client.GetCharacterClientAsync();
var characters = await accountClient.GetCharacters();
foreach (var character in characters)
{
    var details = await characterClient.GetCharacter(character.CharacterId, character.SeasonId);
    Console.WriteLine($"  - {character.Name}: Level {details.Level}");
}

// Dispose when done
await client.DisposeAsync();
```

## Authentication

Authentication uses HTTP endpoints following industry standards:

```csharp
// Login
var result = await client.Auth.LoginAsync("eos-id-token", "EOS");

// Refresh tokens
var refreshed = await client.Auth.RefreshAsync(refreshToken, userId);

// Logout
await client.Auth.LogoutAsync(refreshToken);

// Get available providers
var providers = await client.Auth.GetProvidersAsync();
```

## Hub Clients

| Client | Method | Description |
|--------|--------|-------------|
| Account | `GetAccountClientAsync()` | Account info, cosmetics, achievements |
| Character | `GetCharacterClientAsync()` | Character stats, experience, challenges |
| Inventory | `GetInventoryClientAsync()` | Item management, history |
| Trade | `GetTradeClientAsync()` | Trading operations with real-time updates |
| ItemType | `GetItemTypeClientAsync()` | Item type definitions |
| Season | `GetSeasonClientAsync()` | Season management |
| Auth | `GetAuthHubClientAsync()` | WebSocket-based auth operations |

## Real-Time Trade Updates

```csharp
// Implement the receiver interface
public class MyTradeReceiver : ITradeHubReceiver
{
    public Task TradeUpdate(TradeUpdateEvent update)
    {
        Console.WriteLine($"Trade {update.TradeId}: {update.EventType}");
        return Task.CompletedTask;
    }
}

// Register for callbacks
var receiver = new MyTradeReceiver();
var tradeClient = await client.GetTradeClientAsync(receiver);

// Start a trade
var session = await tradeClient.StartTrade(myCharacterId, targetCharacterId, seasonId);
await tradeClient.JoinTradeSession(session.TradeId);
```

## Dependency Injection

```csharp
// In Startup/Program.cs
services.AddTitanClient(options =>
{
    options.BaseUrl = "https://api.titan.gg";
    options.EnableAutoReconnect = true;
});

// Inject into services
public class GameService
{
    private readonly TitanClient _titan;
    
    public GameService(TitanClient titan) => _titan = titan;
    
    public async Task DoSomethingAsync()
    {
        var account = await (await _titan.GetAccountClientAsync()).GetAccount();
    }
}
```

## Migration from TitanSession (Old API)

The old `TitanSession` class has been replaced with `TitanClient`:

### Before (TitanSession)
```csharp
var session = new TitanSession(baseUrl, accessToken, refreshToken, expiry, userId);
var accountHub = await session.GetAccountHubAsync();
var account = await accountHub.InvokeAsync<Account>("GetAccount"); // Magic string!
```

### After (TitanClient)
```csharp
var client = new TitanClientBuilder()
    .WithBaseUrl(baseUrl)
    .Build();
var result = await client.Auth.LoginAsync(providerToken, "EOS");
var accountClient = await client.GetAccountClientAsync();
var account = await accountClient.GetAccount(); // Type-safe!
```

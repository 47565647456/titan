# Titan.Abstractions

## Overview
**Titan.Abstractions** is the core shared library for the Titan solution. It defines the contracts, data structures, and common types used across both the API gateway and the backend Orleans Silos.

## Role in Global Solution
This project decouples the interface/contract from the implementation. Both `Titan.API` (the client) and `Titan.Grains` (the implementation) reference this project. This allows the API to call methods on Grains without needing a reference to the actual logic, facilitating a clean separation of concerns and enabling the distributed actor model.

## Key Components
- **Grain Interfaces**: Defines the `IGrain` interfaces (e.g., `IInventoryGrain`, `ITradeGrain`) that specify the methods available for remote invocation.
- **Models & DTOs**: Contains data transfer objects and models used for passing data between the client, API, and grains.
- **Configuration Models**: Defines strongly-typed options like `ItemRegistryOptions` (Section: "ItemRegistry") and `TradingOptions` (Section: "Trading") used to configure behavior via `appsettings.json`.
- **Constants**: Shared constants for stream providers, configuration keys, and other system-wide values.
- **Rules**: Defines rule engine interfaces (e.g., `IRule<T>`) used for validation logic shared across layers.

## Dependencies
This project is designed to have minimal dependencies to ensure it remains lightweight and portable across different parts of the stack.

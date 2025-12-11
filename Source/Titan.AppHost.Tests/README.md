# Titan.AppHost.Tests

End-to-End Integration tests for the full Titan System.

## Test Strategy
These tests use `Aspire.Hosting.Testing` to launch the **entire application stack** (AppHost, API, Silos, Redis, Postgres) as a real running environment.

### Features Tested
- **Authentication**: Verifies JWT generation, Admin roles, and SignalR connection security (`AuthenticationTests.cs`).
- **End-to-End Flows**: Tests that require the full HTTP/Socket API and coordination between multiple services.
- **Infrastructure**: Verifies that containers spin up and connect correctly.

## Running Tests
These tests are slower than unit tests as they require spinning up Docker containers and multiple .NET processes.
- Ensure Docker Desktop is running.
- Run via `dotnet test`.

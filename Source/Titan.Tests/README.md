# Titan.Tests

## Overview
**Titan.Tests** contains the integration and end-to-end tests for the Titan backend.

## Role in Global Solution
This project is crucial for ensuring the reliability of the system. It uses `Aspire.Hosting.Testing` to spin up the entire distributed application (in-memory or using containers) and perform verification against the actual running services.

## Key Responsibilities
- **Integration Testing**: Verifies that the API, Silos, Database, and Cache all work together correctly.
- **Scenario Verification**: Tests complex user flows like "Login -> Trade -> Logout" in a realistic environment.
- **CI/CD Integration**: These tests are designed to run in the CI pipeline to prevent regressions.

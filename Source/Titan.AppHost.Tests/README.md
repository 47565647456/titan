# Titan.AppHost.Tests

## Overview
**Titan.AppHost.Tests** contains tests specifically for the AppHost orchestration logic.

## Role in Global Solution
**Current Status: Pending Implementation**
This project is scaffolded to verify that the AppHost is correctly serving dependencies and defining resources, but it currently contains no active tests.
Porting Distributed Tests to Aspire
The goal is to move tests from Titan.Tests (In-Process TestCluster) to Titan.AppHost.Tests (Full Aspire Environment). This ensures our tests run against the actual orchestrated environment used in production.

Future tests will include:
- **Manifest Verification**: Ensuring the distributed application manifest is generated correctly.
- **Resource Configuration**: Verifying that connection strings and environment variables are being passed as expected.

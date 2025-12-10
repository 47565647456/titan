# Titan.IdentityHost

## Overview
**Titan.IdentityHost** is an Orleans Silo executable responsible for hosting identity and metadata-related services.

## Role in Global Solution
As a dedicated Silo, it contributes to the Orleans cluster by hosting grains related to user accounts, authentication, and core game data registries. Segregating these services allows for independent scaling and isolation of critical login/metadata infrastructure.

**Critical Dependency**: This host runs the database seeding logic. The `Titan.API` is configured to wait for this service to be healthy before starting to ensure that reference data (like Item Types) is available.

## Key Responsibilities
- **Grain Hosting**: Hosts `AccountGrain`, `CharacterGrain`, and potentially `ItemTypeRegistryGrain`.
- **Data Seeding**: Contains registered hosted services (like `ItemTypeSeedHostedService`) to ensure static game data is initialized on startup.
- **Cluster Participation**: Joins the Aspire-managed Redis cluster to communicate with other silos and the API.

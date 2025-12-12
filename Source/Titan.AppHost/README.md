# Titan.AppHost

The **Aspire** Orchestrator for the Titan Backend. This project is the entry point for running the entire distributed system locally.

## Responsibilities
- **Orchestration**: Starts and manages all services (API, IdentityHost, InventoryHost, TradingHost).
- **Service Discovery**: Injects connection strings and endpoints into child projects.
- **Infrastructure**: Provisions Docker containers for Redis and CockroachDB.

## Resources

### Databases
- **CockroachDB (`titan-db`)**:
  - Distributed SQL database with PostgreSQL wire protocol.
  - Runs in **secure mode** with auto-generated TLS certificates.
  - Initialized with `scripts/cockroachdb/init.sql`.
  - **Cluster Mode**: Configurable single-node or 3-node cluster via `Database:CockroachCluster`.

### Certificates
The AppHost automatically generates TLS certificates for CockroachDB:
- **CA Certificate**: Signs all other certs
- **Node Certificate**: For database server
- **Client Certificate**: For root user connections

To trust the admin UI in your browser, import the CA cert from the Docker volume (see instructions in root README).

### Caching / Clustering
- **Redis (`orleans-clustering`)**: 
  - Used by Orleans for Silo membership (Clustering).
  - Includes RedisInsight for inspection.

## Configuration Parameters

| Parameter | Description | Default (Dev) |
|-----------|-------------|---------------|
| `cockroachdb-password` | Password for the database user | `TitanDevelopmentPassword123!` |
| `cockroachdb-username` | Database username | `titan` |
| `Database:CockroachCluster` | `single` or `cluster` (3-node) | `single` |
| `Database:Volume` | Docker volume name. Set to `ephemeral` to wipe DB. | `(dynamic)` |

## Running the Project
Set this project as the **Startup Project** in Visual Studio or run `dotnet run` to launch the Aspire Dashboard.

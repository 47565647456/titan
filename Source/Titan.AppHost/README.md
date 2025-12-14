# Titan.AppHost

The **Aspire** Orchestrator for the Titan Backend. This project is the entry point for running the entire distributed system locally.

## Responsibilities
- **Orchestration**: Starts and manages all services (API, IdentityHost, InventoryHost, TradingHost, Dashboard).
- **Service Discovery**: Injects connection strings and endpoints into child projects.
- **Infrastructure**: Provisions Docker containers for Redis and CockroachDB.
- **Security**: Manages TLS certificate generation for secure database communication.

## Resources

### Databases
- **CockroachDB (`titan-db`)**:
  - Distributed SQL database with PostgreSQL wire protocol.
  - Runs in **secure mode** with auto-generated TLS certificates.
  - Initialized with `scripts/cockroachdb/init.sql` and `init_admin.sql`.
  - **Cluster Mode**: Configurable single-node or 3-node cluster via `Database:CockroachCluster`.

### Certificates
The AppHost automatically generates TLS certificates for CockroachDB using a helper container (`cockroach-certs`):
- **CA Certificate**: Signs all other certs.
- **Node Certificate**: For database server nodes.
- **Client Certificate**: For root user connections.

To trust the admin UI in your browser, import the CA cert from the Docker volume (see instructions in root README).

### Caching / Clustering
- **Redis (`orleans-clustering`)**: 
  - Used by Orleans for Silo membership (Clustering) and Grain Directory.
  - Includes RedisInsight for data inspection.

## Configuration Parameters

### Service Orchestration
The AppHost orchestrates the following projects:
- **Silos**: `Titan.IdentityHost`, `Titan.InventoryHost`, `Titan.TradingHost`.
- **API**: `Titan.API` (Gateway).
- **Dashboard**: `Titan.Dashboard`.

### Configuration Parameters

### General
| Parameter | Description | Default |
|-----------|-------------|---------|
| `Orleans:Replicas` | Number of instances per silo host | `2` |
| `Jwt:Key` | Master key for token signing (injected into API) | *(Required)* |

### Database
| Parameter | Description | Default |
|-----------|-------------|---------|
| `cockroachdb-password` | Password for the database user | `TestPassword123ABC` |
| `cockroachdb-username` | Database username | `titan_user` |
| `Database:CockroachCluster` | `single` or `cluster` (3-node) | `cluster` (in appsettings) |
| `Database:Volume` | Docker volume name. Set to `ephemeral` to wipe DB/Certs. | `(dynamic)` |
| `Database:FollowerReads` | Enable follower reads (~4.8s stale) for lower latency | `false` |
| `Database:Pool:MaxPoolSize` | Max connections per silo | `50` |
| `Database:Pool:MinPoolSize` | Min connections (fixed pool) | `50` |
| `Database:Pool:ConnectionLifetimeSeconds` | Max connection age before recycle | `300` |
| `Database:Pool:ConnectionIdleLifetimeSeconds` | Max idle time before close | `300` |

### Container Versions
Override these if you need specific versions.
| Parameter | Description | Default |
|-----------|-------------|---------|
| `ContainerImages:Redis:Image` | Redis image name | `redis` |
| `ContainerImages:Redis:Tag` | Redis image tag | `8.4` |
| `ContainerImages:CockroachDB:Image` | CockroachDB image name | `cockroachdb/cockroach` |
| `ContainerImages:CockroachDB:Tag` | CockroachDB image tag | `latest-v25.4` |

## Running the Project
Set this project as the **Startup Project** in Visual Studio or run `dotnet run` to launch the Aspire Dashboard.
The AppHost will handle the complexity of:
1. Generating Certificates.
2. Waiting for DB initialization.
3. Starting Redis and Silos in the correct order.

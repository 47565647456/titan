# Titan.AppHost

The **Aspire** Orchestrator for the Titan Backend. This project is the entry point for running the entire distributed system locally.

## Responsibilities
- **Orchestration**: Starts and manages all services (API, IdentityHost, InventoryHost, TradingHost, Dashboard).
- **Service Discovery**: Injects connection strings and endpoints into child projects.
- **Infrastructure**: Provisions Docker containers for Redis and PostgreSQL.

## Resources

### Database
- **PostgreSQL (`postgres`)**:
  - PostgreSQL database with **SSL/TLS enabled**.
  - Self-signed certificates auto-generated via `postgres-certs` container.
  - Includes pgAdmin for web-based database management.
  - Initialized with `scripts/postgres/01-init-orleans.sql` and `02-init-admin.sql`.
  - Data persisted in Docker volume.

### SSL/TLS Certificates
The AppHost automatically generates self-signed SSL certificates for PostgreSQL:
- **Server Certificate**: `/etc/postgresql/certs/server.crt`
- **Server Key**: `/etc/postgresql/certs/server.key`

Certificates are stored in a Docker volume and reused across restarts (unless `Database:Volume=ephemeral`).

### Caching / Clustering
- **Redis (`orleans-clustering`)**: 
  - Used by Orleans for Silo membership (Clustering) and Grain Directory.
  - Includes RedisInsight for visualization.
- **Redis (`rate-limiting`)**:
  - Dedicated instance for tracking request counts and timeout state.
  - Separate from clustering to avoid interference with core cluster stability.

## Configuration Parameters

### Service Orchestration
The AppHost orchestrates the following projects:
- **Silos**: `Titan.IdentityHost`, `Titan.InventoryHost`, `Titan.TradingHost`.
- **API**: `Titan.API` (Gateway).
- **Dashboard**: `Titan.Dashboard`.

### General
| Parameter | Description | Default |
|-----------|-------------|---------|
| `Orleans:Replicas` | Number of instances per silo host | `2` |
| `Jwt:Key` | Master key for token signing (injected into API) | *(Required)* |

### Database
| Parameter | Description | Default |
|-----------|-------------|---------|
| `postgres-password` | Password for the database user | `TestPassword123ABC` |
| `Database:Volume` | Docker volume name. Set to `ephemeral` to use temporary storage. | `titan-postgres-data-{env}` |
| `Database:Pool:MaxPoolSize` | Max connections per silo | `100` |
| `Database:Pool:MinPoolSize` | Min connections | `0` |
| `Database:Pool:ConnectionLifetimeSeconds` | Max connection age before recycle | `300` |
| `Database:Pool:ConnectionIdleLifetimeSeconds` | Max idle time before close | `300` |

### Container Versions
Override these if you need specific versions.
| Parameter | Description | Default |
|-----------|-------------|---------|
| `ContainerImages:Redis:Image` | Redis image name | `redis` |
| `ContainerImages:Redis:Tag` | Redis image tag | `8.4` |
| `ContainerImages:Postgres:Image` | PostgreSQL image name | `postgres` |
| `ContainerImages:Postgres:Tag` | PostgreSQL image tag | `17` |

## Running the Project
Set this project as the **Startup Project** in Visual Studio or run `dotnet run` to launch the Aspire Dashboard.
The AppHost will handle starting PostgreSQL, Redis, and Silos in the correct order.

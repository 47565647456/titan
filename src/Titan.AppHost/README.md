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
  - Includes RedisInsight.

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
<<<<<<< HEAD:src/Titan.AppHost/README.md
The AppHost will handle starting PostgreSQL, Redis, and Silos in the correct order.
=======
The AppHost will handle the complexity of:
1. Generating Certificates.
2. Waiting for DB initialization.
3. Starting Redis and Silos in the correct order.

## Backup and Restore

### Configuration
Backup settings in `appsettings.json`:

| Parameter | Description | Default |
|-----------|-------------|---------|
| `Backup:Provider` | Storage: `userfile`, `s3`, `gcs`, `azure` | `userfile` |
| `Backup:RevisionHistory` | Enable point-in-time restore | `true` |
| `Backup:Schedule:Enabled` | Enable automated backups | `false` |
| `Backup:Schedule:IncrementalCron` | Incremental backup schedule | `0 * * * *` (hourly) |
| `Backup:Schedule:FullBackupFrequency` | Full backup frequency | `@daily` |
| `Backup:S3:*` | AWS S3 credentials | *(empty)* |
| `Backup:GCS:*` | Google Cloud Storage credentials | *(empty)* |
| `Backup:Azure:*` | Azure Blob Storage credentials | *(empty)* |

### Running Backups

**Using PowerShell script:**
```powershell
# Local backup (userfile)
.\scripts\cockroachdb\backup.ps1

# Backup to S3
.\scripts\cockroachdb\backup.ps1 -Provider s3 -S3Bucket "my-bucket" -S3AccessKey "XXX" -S3SecretKey "YYY"

# Backup without revision history (smaller, faster)
.\scripts\cockroachdb\backup.ps1 -RevisionHistory:$false
```

**Using SQL directly:**
```sql
BACKUP DATABASE titan INTO 'userfile:///titan-backup' AS OF SYSTEM TIME '-10s' WITH revision_history;
```

### Restoring from Backup

**Latest backup:**
```sql
RESTORE DATABASE titan FROM LATEST IN 'userfile:///titan-backup';
```

**Point-in-time restore** (requires `revision_history`):
```sql
RESTORE DATABASE titan FROM LATEST IN 'userfile:///titan-backup' 
  AS OF SYSTEM TIME '2025-12-15 10:00:00';
```

> **Note**: You must drop the existing database before restoring: `DROP DATABASE titan CASCADE;`

### Automated Backups

Enable automated backups by setting `Backup:Schedule:Enabled` to `true` in `appsettings.json`. Schedules are created when the AppHost initializes.

**Managing schedules:**
```sql
-- View all backup schedules
SHOW SCHEDULES FOR BACKUP;

-- Pause a schedule
PAUSE SCHEDULE <schedule_id>;

-- Resume a schedule
RESUME SCHEDULE <schedule_id>;

-- Check backup job status
SHOW JOBS WHERE job_type = 'BACKUP';
```
>>>>>>> 9c360f2f4a645a202c012e878c444d65b81649ee:Source/Titan.AppHost/README.md

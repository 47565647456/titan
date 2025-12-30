using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Titan.AppHost;

/// <summary>
/// Helper class for database resource configuration in the Aspire AppHost.
/// Configures PostgreSQL with SSL for secure connections.
/// </summary>
public static class DatabaseResources
{
    private const string CertsVolumeName = "titan-postgres-certs";
    private const string CertsMountPath = "/etc/postgresql/certs";

    /// <summary>
    /// Adds PostgreSQL database resources to the Aspire application.
    /// Returns the main database, admin database, and initialization project.
    /// Configures SSL/TLS for encrypted connections.
    /// </summary>
    public static (IResourceBuilder<PostgresDatabaseResource> Database,
                   IResourceBuilder<PostgresDatabaseResource> AdminDatabase,
                   IResourceBuilder<ProjectResource> DbInit) AddDatabase(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        string env)
    {
        var postgresImage = builder.Configuration["ContainerImages:Postgres:Image"] ?? "postgres";
        var postgresTag = builder.Configuration["ContainerImages:Postgres:Tag"] ?? "17";

        var volumeConfig = builder.Configuration["Database:Volume"];
        var isEphemeral = volumeConfig?.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) == true ||
                          volumeConfig?.Equals("none", StringComparison.OrdinalIgnoreCase) == true;

        IResourceBuilder<ContainerResource>? certsGen = null;
        
        if (isEphemeral)
        {
            // Ephemeral mode (tests) - skip SSL for speed and no lingering volumes
            Console.WriteLine("📦 Using PostgreSQL (ephemeral mode, no SSL) as database backend");
        }
        else
        {
            // Production mode - use SSL with persistent volume for certificates
            Console.WriteLine("📦 Using PostgreSQL (SSL enabled) as database backend");
            certsGen = AddPostgresCerts(builder, CertsVolumeName);
        }

        var postgres = builder.AddPostgres("postgres", password: password)
            .WithImage(postgresImage, postgresTag)
            .WithPgAdmin();

        // Configure SSL only for non-ephemeral mode
        if (!isEphemeral && certsGen != null)
        {
            postgres
                .WaitForCompletion(certsGen)
                .WithVolume(CertsVolumeName, CertsMountPath);
            
            // Configure PostgreSQL to use SSL via command-line args
            var maxConnections = builder.Configuration.GetValue("Database:Server:MaxConnections", 500);
            var sharedBuffersMb = builder.Configuration.GetValue("Database:Server:SharedBuffersMB", 256);
            
            postgres.WithArgs(
                "-c", "ssl=on",
                "-c", $"ssl_cert_file={CertsMountPath}/server.crt",
                "-c", $"ssl_key_file={CertsMountPath}/server.key",
                "-c", $"max_connections={maxConnections}",
                "-c", $"shared_buffers={sharedBuffersMb}MB"
            );
        }
        else
        {
            // Non-SSL mode (ephemeral) - just configure connection limits
            var maxConnections = builder.Configuration.GetValue("Database:Server:MaxConnections", 500);
            var sharedBuffersMb = builder.Configuration.GetValue("Database:Server:SharedBuffersMB", 256);
            
            postgres.WithArgs(
                "-c", $"max_connections={maxConnections}",
                "-c", $"shared_buffers={sharedBuffersMb}MB"
            );
        }

        // Configure data persistence (only for non-ephemeral mode)
        if (!isEphemeral)
        {
            if (string.IsNullOrEmpty(volumeConfig))
            {
                postgres.WithDataVolume($"titan-postgres-data-{env}");
            }
            else
            {
                postgres.WithDataVolume(volumeConfig);
            }
        }

        // Create databases first
        var titanDb = postgres.AddDatabase("titan");
        var titanAdminDb = postgres.AddDatabase("titan-admin");

        // Database initialization using .NET console app (K8s compatible)
        var dbInit = AddDatabaseInit(builder, titanDb, titanAdminDb);

        return (titanDb, titanAdminDb, dbInit);
    }

    /// <summary>
    /// Creates a database initialization project that applies SQL scripts and EF migrations.
    /// Uses a .NET console app for Kubernetes compatibility.
    /// </summary>
    private static IResourceBuilder<ProjectResource> AddDatabaseInit(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> titanDb,
        IResourceBuilder<PostgresDatabaseResource> titanAdminDb)
    {
        return builder.AddProject<Projects.Titan_DbMigrator>("postgres-init")
            .WithReference(titanDb)        // Orleans schema goes here
            .WithReference(titanAdminDb)   // Admin Identity schema goes here
            .WaitFor(titanDb)
            .WaitFor(titanAdminDb);
    }

    /// <summary>
    /// Creates a helper container that generates SSL certificates for PostgreSQL.
    /// Generates a proper CA certificate chain:
    /// - ca.key / ca.crt: Certificate Authority (for client verification)
    /// - server.key / server.crt: Server certificate signed by the CA
    /// The certificates are stored in a shared volume and mounted into the PostgreSQL container.
    /// </summary>
    private static IResourceBuilder<ContainerResource> AddPostgresCerts(
        IDistributedApplicationBuilder builder,
        string certsVolumeName)
    {
        var opensslImage = builder.Configuration["ContainerImages:OpenSSL:Image"] ?? "alpine/openssl";
        var opensslTag = builder.Configuration["ContainerImages:OpenSSL:Tag"] ?? "latest";

        // Script to generate CA certificate chain using OpenSSL
        // Step 1: Generate CA private key and self-signed CA certificate
        // Step 2: Generate server private key and CSR
        // Step 3: Sign server certificate with CA
        // Sets permissions to 600 (required by PostgreSQL) and ownership to postgres user (UID 999)
        var genScript =
            $"mkdir -p {CertsMountPath} && " +
            $"if [ ! -f {CertsMountPath}/server.crt ]; then " +
            $"echo 'Generating PostgreSQL CA and server certificates...' && " +
            // Generate CA private key
            $"openssl genrsa -out {CertsMountPath}/ca.key 4096 && " +
            // Generate self-signed CA certificate
            $"openssl req -new -x509 -days 3650 -key {CertsMountPath}/ca.key -out {CertsMountPath}/ca.crt -subj '/CN=TitanPostgresCA/O=Titan/C=AU' && " +
            // Generate server private key
            $"openssl genrsa -out {CertsMountPath}/server.key 4096 && " +
            // Generate server CSR
            $"openssl req -new -key {CertsMountPath}/server.key -out {CertsMountPath}/server.csr -subj '/CN=postgres/O=Titan/C=AU' && " +
            // Sign server certificate with CA (valid for 365 days)
            $"openssl x509 -req -days 365 -in {CertsMountPath}/server.csr -CA {CertsMountPath}/ca.crt -CAkey {CertsMountPath}/ca.key -CAcreateserial -out {CertsMountPath}/server.crt && " +
            // Set permissions (600 for keys, 644 for certs)
            $"chmod 600 {CertsMountPath}/ca.key {CertsMountPath}/server.key && " +
            $"chmod 644 {CertsMountPath}/ca.crt {CertsMountPath}/server.crt && " +
            $"chown 999:999 {CertsMountPath}/server.key {CertsMountPath}/server.crt {CertsMountPath}/ca.key {CertsMountPath}/ca.crt && " +
            // Cleanup CSR
            $"rm -f {CertsMountPath}/server.csr && " +
            $"echo 'SSL certificate chain generated successfully:' && " +
            $"ls -la {CertsMountPath}; " +
            $"else " +
            $"echo 'SSL certificates already exist.'; " +
            $"fi";

        return builder.AddContainer("postgres-certs", opensslImage, opensslTag)
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", genScript)
            .WithVolume(certsVolumeName, CertsMountPath);
    }
}

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Titan.AppHost;

/// <summary>
/// Helper class for database resource configuration in the Aspire AppHost.
/// New database backends can be added by implementing new static methods following the pattern.
/// </summary>
public static class DatabaseResources
{
    private const string CertsVolumeName = "titan-cockroachdb-certs";
    private const string CertsMountPath = "/cockroach/certs";
    private const string SafeDir = "/cockroach/safe";

    /// <summary>
    /// Adds the configured database resource to the Aspire application.
    /// </summary>
    public static (IResourceBuilder<IResourceWithConnectionString> Database, IResourceBuilder<ContainerResource>? Container) AddDatabase(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        IResourceBuilder<ParameterResource> username,
        string env)
    {
        return AddCockroachDB(builder, password, username, env);
    }

    /// <summary>
    /// Adds a wait dependency on a custom database container if applicable.
    /// </summary>
    public static void AddDbWait<T>(
        IResourceBuilder<T> project,
        IResourceBuilder<ContainerResource>? dbContainer)
        where T : IResourceWithWaitSupport
    {
        if (dbContainer != null)
        {
            project.WaitFor(dbContainer);
        }
    }

    /// <summary>
    /// Adds a CockroachDB cluster (single or multi-node) in SECURE mode.
    /// 1. Generates CA, Node, and Client certs in a helper container.
    /// 2. Starts CockroachDB with --certs-dir.
    /// 3. Initializes the cluster and sets the root password.
    /// </summary>
    public static (IResourceBuilder<IResourceWithConnectionString> ConnectionString, IResourceBuilder<ContainerResource> Container) AddCockroachDB(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        IResourceBuilder<ParameterResource> username,
        string env)
    {
        var cockroachImage = builder.Configuration["ContainerImages:CockroachDB:Image"] ?? "cockroachdb/cockroach";
        var cockroachTag = builder.Configuration["ContainerImages:CockroachDB:Tag"] ?? "latest-v24.3";
        var clusterMode = builder.Configuration["Database:CockroachCluster"]?.ToLowerInvariant() ?? "single";

        var volumeConfig = builder.Configuration["Database:Volume"];
        var isEphemeral = volumeConfig?.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) == true ||
                          volumeConfig?.Equals("none", StringComparison.OrdinalIgnoreCase) == true;

        string certsSource;
        bool isBindMount;

        if (isEphemeral)
        {
            // Use a temporary directory for certs in ephemeral mode to avoid polluting Docker volumes
            // and ensure cleanup (OS handles temp, or it's just files)
            certsSource = Path.Combine(Path.GetTempPath(), $"titan-certs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(certsSource);
            isBindMount = true;
        }
        else
        {
            certsSource = CertsVolumeName;
            isBindMount = false;
        }

        // 1. Certificate Generator
        var certsGen = AddCockroachCerts(builder, cockroachImage, cockroachTag, isEphemeral, certsSource, isBindMount);

        IResourceBuilder<ContainerResource> primaryNode;
        IResourceBuilder<ContainerResource>? clusterInit = null;

        // 2. Database Nodes
        if (clusterMode == "cluster")
        {
            Console.WriteLine("📦 Using CockroachDB 3-node cluster (SECURE) as database backend");
            (primaryNode, clusterInit) = AddCockroachDBCluster(builder, cockroachImage, cockroachTag, env, volumeConfig, certsGen, certsSource, isBindMount);
        }
        else
        {
            Console.WriteLine("📦 Using CockroachDB single-node (SECURE) as database backend");
            var cockroach = builder.AddContainer("titan-db", cockroachImage, cockroachTag)
                .WithArgs("start-single-node", $"--certs-dir={CertsMountPath}", "--advertise-addr=localhost")
                .WithEndpoint(port: 26257, targetPort: 26257, name: "sql")
                .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
                .WithHttpHealthCheck("/health?ready=1", endpointName: "http")
                .WithExternalHttpEndpoints()
                .WaitFor(certsGen);

            if (isBindMount) cockroach.WithBindMount(certsSource, CertsMountPath);
            else cockroach.WithVolume(certsSource, CertsMountPath);

            // Volume configuration
            if (isEphemeral)
            {
                cockroach.WithVolume(name: null!, "/cockroach/cockroach-data");
            }
            else if (string.IsNullOrEmpty(volumeConfig))
            {
                cockroach.WithVolume($"titan-cockroachdb-data-{env}", "/cockroach/cockroach-data");
            }
            else
            {
                cockroach.WithVolume(volumeConfig, "/cockroach/cockroach-data");
            }

            primaryNode = cockroach;
        }

        // 3. Cluster/Schema Initialization & Password Setup
        // Uses client.root certs to connect, creates user with password, then runs init.sql
        var setupScript = 
            $"echo 'Waiting for DB...'; " +
            $"until ./cockroach sql --certs-dir={CertsMountPath} --host=titan-db --execute='SELECT 1'; do sleep 1; done; " +
            $"echo 'DB Ready. Setting up user...'; " +
            $"./cockroach sql --certs-dir={CertsMountPath} --host=titan-db --execute=\"CREATE USER IF NOT EXISTS $DB_USER WITH PASSWORD '$DB_PASSWORD'; GRANT ADMIN TO $DB_USER;\"; " +
            $"echo 'Running init.sql...'; " +
            $"./cockroach sql --certs-dir={CertsMountPath} --host=titan-db --file=/init.sql; " +
            $"echo 'Initialization Complete.';";

        var orleansInit = builder.AddContainer("cockroachdb-init", cockroachImage, cockroachTag)
            .WithBindMount("../../scripts/cockroachdb/init.sql", "/init.sql")
            .WithEnvironment("DB_PASSWORD", password)
            .WithEnvironment("DB_USER", username)
            .WithEntrypoint("/bin/bash")
            .WithArgs("-c", setupScript)
            .WaitFor(primaryNode);

        if (isBindMount) orleansInit.WithBindMount(certsSource, CertsMountPath);
        else orleansInit.WithVolume(certsSource, CertsMountPath);

        if (clusterInit != null) orleansInit.WaitFor(clusterInit);

        // 4. Connection String (Secure) with Configurable Pooling
        // Uses Configured User + Password + Trust Server Certificate + Connection Pool Settings
        var endpoint = primaryNode.GetEndpoint("sql");
        
        // Read pool settings from configuration with CockroachDB-recommended defaults
        var poolConfig = builder.Configuration.GetSection("Database:Pool");
        var maxPoolSize = poolConfig["MaxPoolSize"] ?? "50";
        var minPoolSize = poolConfig["MinPoolSize"] ?? "50";
        var connectionLifetime = poolConfig["ConnectionLifetimeSeconds"] ?? "300";
        var connectionIdleLifetime = poolConfig["ConnectionIdleLifetimeSeconds"] ?? "300";
        
        // Follower reads: Add Options parameter to enable for read-heavy connections
        // This sets 'default_transaction_use_follower_reads = on' at session level
        // Trades ~4.8s staleness for reduced read latency in multi-region clusters
        var followerReadsEnabled = builder.Configuration.GetValue("Database:FollowerReads", false);
        var options = followerReadsEnabled 
            ? ";Options=--default_transaction_use_follower_reads=on"
            : "";
        
        var connectionString = builder.AddConnectionString(
            "titan",
            ReferenceExpression.Create(
                $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database=titan;Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;ApplicationName=Titan;MaxPoolSize={maxPoolSize};MinPoolSize={minPoolSize};ConnectionLifetime={connectionLifetime};ConnectionIdleLifetime={connectionIdleLifetime}{options}"));

        return (connectionString, orleansInit);
    }

    /// <summary>
    /// creates a helper container that generates the CA, Node, and Client certificates
    /// if they don't already exist in the shared volume.
    /// </summary>
    private static IResourceBuilder<ContainerResource> AddCockroachCerts(
        IDistributedApplicationBuilder builder, 
        string image, 
        string tag,
        bool isEphemeral,
        string certsSource,
        bool isBindMount)
    {
        // Script to generate certs
        // 1. Create CA if missing
        // 2. Create Node certs (valid for all node names + localhost)
        // 3. Create Client root cert
        var genScript =
            $"mkdir -p {CertsMountPath} {SafeDir}; " +
            $"if [ ! -f {CertsMountPath}/ca.crt ]; then " +
            $"  echo 'Generating CA...'; " +
            $"  cockroach cert create-ca --certs-dir={CertsMountPath} --ca-key={SafeDir}/ca.key; " +
            $"  echo 'Generating Node Certs...'; " +
            $"  cockroach cert create-node titan-db titan-db-2 titan-db-3 localhost 127.0.0.1 --certs-dir={CertsMountPath} --ca-key={SafeDir}/ca.key; " +
            $"  echo 'Generating Client Root Cert...'; " +
            $"  cockroach cert create-client root --certs-dir={CertsMountPath} --ca-key={SafeDir}/ca.key; " +
            $"else " +
            $"  echo 'Certificates already exist.'; " +
            $"fi; " +
            $"chmod 700 {CertsMountPath}/*.key; " + // Secure permissions
            $"ls -la {CertsMountPath}; " +
            $"sleep infinity;"; // Keep container running so dependencies can wait for it

        var resource = builder.AddContainer("cockroach-certs", image, tag)
            .WithEntrypoint("/bin/bash")
            .WithArgs("-c", genScript);
            
        if (isBindMount) resource.WithBindMount(certsSource, CertsMountPath);
        else resource.WithVolume(certsSource, CertsMountPath);
        
        if (isEphemeral)
        {
            resource.WithVolume(name: null!, target: SafeDir);
        }
        else
        {
            resource.WithVolume("titan-cockroachdb-safe", SafeDir);
        }

        return resource;
    }

    private static (IResourceBuilder<ContainerResource> Node, IResourceBuilder<ContainerResource> ClusterInit) AddCockroachDBCluster(
        IDistributedApplicationBuilder builder,
        string image,
        string tag,
        string env,
        string? volumeConfig,
        IResourceBuilder<ContainerResource> certs,
        string certsSource,
        bool isBindMount)
    {
        var joinAddrs = "titan-db:26257,titan-db-2:26257,titan-db-3:26257";
        var isEphemeral = volumeConfig?.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) == true ||
                          volumeConfig?.Equals("none", StringComparison.OrdinalIgnoreCase) == true;

        // Node 1
        var node1 = builder.AddContainer("titan-db", image, tag)
            .WithArgs("start", $"--certs-dir={CertsMountPath}", "--advertise-addr=titan-db:26257", $"--join={joinAddrs}")
            .WithEndpoint(port: 26257, targetPort: 26257, name: "sql")
            .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
            .WithHttpHealthCheck("/health", endpointName: "http")
            .WithExternalHttpEndpoints()
            .WaitFor(certs);
            
        if (isBindMount) node1.WithBindMount(certsSource, CertsMountPath);
        else node1.WithVolume(certsSource, CertsMountPath);
            
        node1.WithVolume(isEphemeral ? null! : $"titan-cockroachdb-1-{env}", "/cockroach/cockroach-data");

        // Node 2
        var node2 = builder.AddContainer("titan-db-2", image, tag)
            .WithArgs("start", $"--certs-dir={CertsMountPath}", "--advertise-addr=titan-db-2:26257", $"--join={joinAddrs}")
            .WithEndpoint(targetPort: 26257, name: "sql")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithHttpHealthCheck("/health", endpointName: "http")
            .WaitFor(certs);

        if (isBindMount) node2.WithBindMount(certsSource, CertsMountPath);
        else node2.WithVolume(certsSource, CertsMountPath);

        node2.WithVolume(isEphemeral ? null! : $"titan-cockroachdb-2-{env}", "/cockroach/cockroach-data");

        // Node 3
        var node3 = builder.AddContainer("titan-db-3", image, tag)
            .WithArgs("start", $"--certs-dir={CertsMountPath}", "--advertise-addr=titan-db-3:26257", $"--join={joinAddrs}")
            .WithEndpoint(targetPort: 26257, name: "sql")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithHttpHealthCheck("/health", endpointName: "http")
            .WaitFor(certs);

        if (isBindMount) node3.WithBindMount(certsSource, CertsMountPath);
        else node3.WithVolume(certsSource, CertsMountPath);
        
        node3.WithVolume(isEphemeral ? null! : $"titan-cockroachdb-3-{env}", "/cockroach/cockroach-data");

        // Init cluster
        var clusterInit = builder.AddContainer("cockroachdb-cluster-init", image, tag)
            .WithArgs("init", $"--certs-dir={CertsMountPath}", "--host=titan-db:26257")
            .WaitFor(node1)
            .WaitFor(node2)
            .WaitFor(node3);
            
        if (isBindMount) clusterInit.WithBindMount(certsSource, CertsMountPath);
        else clusterInit.WithVolume(certsSource, CertsMountPath);

        return (node1, clusterInit);
    }
}

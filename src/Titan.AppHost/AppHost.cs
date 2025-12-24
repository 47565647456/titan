using Aspire.Hosting.Yarp.Transforms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Titan.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var env = builder.Environment.EnvironmentName.ToLowerInvariant();

// =============================================================================
// Container Image Configuration
// =============================================================================

// Read container image versions from configuration (defaults to pinned stable versions)
var redisImage = builder.Configuration["ContainerImages:Redis:Image"] ?? "redis";
var redisTag = builder.Configuration["ContainerImages:Redis:Tag"] ?? "7.4";

// =============================================================================
// Infrastructure Resources
// =============================================================================

// Redis for Orleans clustering (silo membership)
var redis = builder.AddRedis("orleans-clustering")
    .WithImage(redisImage, redisTag)
    .WithRedisInsight();

// Redis for rate limiting state (separate from clustering)
var rateLimitRedis = builder.AddRedis("rate-limiting")
    .WithImage(redisImage, redisTag);

// Redis for session storage (separate from rate limiting for isolation)
var sessionsRedis = builder.AddRedis("sessions")
    .WithImage(redisImage, redisTag);

// Redis for encryption state (signing keys, per-user encryption state)
var encryptionRedis = builder.AddRedis("encryption")
    .WithImage(redisImage, redisTag);

// Database password
var dbPassword = builder.AddParameter("postgres-password");

// Database resource - PostgreSQL (returns both titan and titan-admin connections)
var (titanDb, titanAdminDb) = DatabaseResources.AddDatabase(builder, dbPassword, env);

// =============================================================================
// Orleans Cluster Configuration
// =============================================================================

var orleans = builder.AddOrleans("titan-cluster")
    .WithClustering(redis);

// =============================================================================
// Orleans Silo Hosts (with replicas for distributed testing)
// =============================================================================

// Get the current environment to propagate to child projects
var environment = builder.Environment.EnvironmentName;

// Configurable replica count for Orleans silos (default: 2)
var replicas = builder.Configuration.GetValue("Orleans:Replicas", 2);

var identityHost = builder.AddProject<Projects.Titan_IdentityHost>("identity-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(redis)  // Orleans clustering requires Redis
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(redis)  // Orleans clustering requires Redis
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(redis)  // Orleans clustering requires Redis
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);

// =============================================================================
// API Gateway (Orleans Client)
// =============================================================================

var api = builder.AddProject<Projects.Titan_API>("api")
    .WithReference(orleans.AsClient())
    .WithReference(titanDb)
    .WithReference(titanAdminDb)  // Admin Identity database for dashboard auth
    .WithReference(rateLimitRedis) // Rate limiting state
    .WithReference(sessionsRedis)  // Session storage
    .WithReference(encryptionRedis)  // Encryption state persistence
    .WaitFor(identityHost)  // Wait for at least one silo to be running
    .WaitFor(titanDb)       // Main game database
    .WaitFor(rateLimitRedis)
    .WaitFor(sessionsRedis) // Wait for session storage to be ready
    .WaitFor(encryptionRedis) // Wait for encryption storage to be ready
    .WaitFor(titanAdminDb)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", environment)
    .WithEnvironment("RateLimiting__Enabled", builder.Configuration["RateLimiting:Enabled"] ?? "true")
    .WithEnvironment("Encryption__Enabled", builder.Configuration["Encryption:Enabled"] ?? "true")
    .WithEnvironment("Encryption__RequireEncryption", builder.Configuration["Encryption:RequireEncryption"] ?? "false");

// =============================================================================
// Admin Dashboard (Containerized React SPA)
// =============================================================================

// Dashboard runs in a container: Node.js builds the Vite app, Nginx serves static files
var dashboard = builder.AddDockerfile("dashboard", "../titan-dashboard")
    .WithImageTag("latest")  // Use consistent tag to avoid image cache bloat
    .WithHttpEndpoint(name: "http", targetPort: 80)
    .WaitFor(api);

// =============================================================================
// YARP Gateway (reverse proxy and routing)
// =============================================================================

var gatewayRoutes = builder.Configuration.GetSection("Gateway:Routes");

var gateway = builder.AddYarp("gateway")
    .WithConfiguration(yarp =>
    {
        // API routes - explicit paths for security
        if (gatewayRoutes.GetValue("Api:Enabled", true))
        {
            // REST API endpoints
            yarp.AddRoute("/api/auth/{**path}", api);           // User authentication
            yarp.AddRoute("/api/admin/{**path}", api);          // Admin dashboard API
            
            // SignalR hubs - need wildcard for /negotiate, /connect, etc.
            yarp.AddRoute("/hub/account/{**path}", api);
            yarp.AddRoute("/hub/auth/{**path}", api);
            yarp.AddRoute("/hub/character/{**path}", api);
            yarp.AddRoute("/hub/inventory/{**path}", api);
            yarp.AddRoute("/hub/base-type/{**path}", api);
            yarp.AddRoute("/hub/season/{**path}", api);
            yarp.AddRoute("/hub/trade/{**path}", api);
            yarp.AddRoute("/hub/broadcast/{**path}", api);
            yarp.AddRoute("/hub/encryption/{**path}", api);
            yarp.AddRoute("/hub/admin-metrics/{**path}", api);
            
            // Health endpoint for dashboard system status
            yarp.AddRoute("/health", api);
        }

        // Dashboard SPA - needs catch-all for client-side routing
        if (gatewayRoutes.GetValue("Dashboard:Enabled", true))
        {
            yarp.AddRoute("/dashboard/{**catch-all}", dashboard.GetEndpoint("http"));
        }
    })
    .WaitFor(api)
    .WaitFor(dashboard);

// =============================================================================
// Production TLS Terminator (Caddy with Let's Encrypt DNS Challenge)
// =============================================================================

if (!builder.Environment.IsDevelopment())
{
    var gatewayConfig = builder.Configuration.GetSection("Gateway");
    var cfApiToken = builder.Configuration["Parameters:cloudflare-api-token"];
    
    // Only add Caddy TLS terminator if Cloudflare API token is configured
    if (!string.IsNullOrEmpty(cfApiToken))
    {
        var caddyImage = builder.Configuration["ContainerImages:Caddy:Image"] ?? "caddybuilds/caddy-cloudflare";
        var caddyTag = builder.Configuration["ContainerImages:Caddy:Tag"] ?? "2.10.2";
        var cfToken = builder.AddParameter("cloudflare-api-token", secret: true);

        var tls = builder.AddContainer("tls", caddyImage, caddyTag)
            .WithReference(gateway)
            .WithEnvironment("DOMAIN", gatewayConfig["Domain"] ?? throw new InvalidOperationException("Gateway:Domain must be configured for production"))
            .WithEnvironment("ACME_EMAIL", gatewayConfig["AcmeEmail"] ?? throw new InvalidOperationException("Gateway:AcmeEmail must be configured for production"))
            .WithEnvironment("CF_API_TOKEN", cfToken)
            .WithEnvironment("ACME_CA", gatewayConfig["AcmeCa"] ?? "") // Empty = production Let's Encrypt
            .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http")) // For Caddyfile reverse_proxy
            .WithBindMount("./caddy/Caddyfile", "/etc/caddy/Caddyfile")
            .WithVolume("titan-caddy-data", "/data")
            .WithHttpsEndpoint(port: 443, targetPort: 443)
            .WithExternalHttpEndpoints()
            .WaitFor(gateway);

        Console.WriteLine($"🔐 Production mode: Caddy TLS terminator ({caddyImage}:{caddyTag}) with Let's Encrypt DNS challenge");
    }
    else
    {
        // No Cloudflare token - expose gateway directly (for local production testing)
        gateway.WithExternalHttpEndpoints();
        Console.WriteLine("⚠️ Production mode: No Cloudflare API token - gateway exposed without TLS termination");
    }

    // Production: YARP trusts dev certs for internal connections
    gateway.WithDeveloperCertificateTrust(trust: true)
           .WithCertificateTrustScope(CertificateTrustScope.System);
}
else
{
    // Development: YARP serves directly with external endpoints and dev certs
    gateway.WithExternalHttpEndpoints()
           .WithDeveloperCertificateTrust(trust: true)
           .WithCertificateTrustScope(CertificateTrustScope.System);
    Console.WriteLine("🔓 Development mode: YARP gateway with dev certificate");
}

builder.Build().Run();


using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

namespace Titan.AppHost;

/// <summary>
/// Helper class for database resource configuration in the Aspire AppHost.
/// New database backends can be added by implementing new static methods following the pattern.
/// </summary>
public static class DatabaseResources
{
    /// <summary>
    /// Adds the configured database resource to the Aspire application.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="password">The database password parameter.</param>
    /// <param name="env">The environment name (used for volume naming).</param>
    /// <param name="databaseType">The database type from configuration (e.g., "postgres").</param>
    /// <returns>A tuple containing the database resource and optional container (for custom containers).</returns>
    public static (IResourceBuilder<IResourceWithConnectionString> Database, IResourceBuilder<ContainerResource>? Container) AddDatabase(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        string env,
        string databaseType)
    {
        return databaseType switch
        {
            "postgres" => (AddPostgres(builder, password, env), null),
            // Add new database types here:
            // "mysql" => (AddMySql(builder, password, env), null),
            // "cockroach" => AddCockroachDb(builder, password, env),
            _ => throw new NotSupportedException($"Database type '{databaseType}' is not supported. Supported types: postgres")
        };
    }

    /// <summary>
    /// Adds a PostgreSQL resource to the Aspire application.
    /// </summary>
    public static IResourceBuilder<IResourceWithConnectionString> AddPostgres(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        string env)
    {
        Console.WriteLine("📦 Using PostgreSQL as database backend");

        var postgresImage = builder.Configuration["ContainerImages:Postgres:Image"] ?? "postgres";
        var postgresTag = builder.Configuration["ContainerImages:Postgres:Tag"] ?? "17";

        var postgres = builder.AddPostgres("postgres", password: password)
            .WithImage(postgresImage, postgresTag)
            .WithPgAdmin()
            .WithInitFiles("../../scripts");

        var volumeName = builder.Configuration["Database:Volume"];
        if (string.IsNullOrEmpty(volumeName))
        {
            postgres.WithDataVolume($"titan-postgres-data-{env}");
        }
        else if (!volumeName.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) &&
                 !volumeName.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            postgres.WithDataVolume(volumeName);
        }

        return postgres.AddDatabase("titan");
    }

    /// <summary>
    /// Checks if the default development password is being used in production.
    /// </summary>
    public static void CheckProductionPassword(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> dbPassword)
    {
        if (builder.Environment.IsProduction() &&
            builder.Configuration["Parameters:postgres-password"] == "TitanDevelopmentPassword123!")
        {
            Console.WriteLine("⚠️  WARNING: You are using the default development password in Production! Please set a secure password via the 'postgres-password' parameter.");
        }
    }

    /// <summary>
    /// Adds a wait dependency on a custom database container if applicable.
    /// For built-in Aspire resources like PostgreSQL, WaitFor(titanDb) handles it.
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
}

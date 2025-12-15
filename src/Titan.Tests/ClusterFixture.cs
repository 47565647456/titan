using Orleans.TestingHost;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Shared test cluster fixture for Orleans grain tests.
/// This fixture creates a single test cluster that is shared across all tests in the collection.
/// </summary>
public class ClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
    }
}

/// <summary>
/// Collection definition for tests that share the same cluster.
/// </summary>
[CollectionDefinition(Name)]
public class ClusterCollection : ICollectionFixture<ClusterFixture>
{
    public const string Name = "ClusterCollection";
}

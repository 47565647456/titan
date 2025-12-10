namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for infrastructure resource health.
/// </summary>
[Collection("AppHost")]
public class ResourceTests : IntegrationTestBase
{
    public ResourceTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Api_HealthCheck_ReturnsHealthy()
    {
        // Arrange - Use CreateHttpClient as recommended by Aspire docs
        using var httpClient = App.CreateHttpClient("api");

        // Act
        var response = await httpClient.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task AllSilos_AreHealthy()
    {
        // This test passes if we got here - the fixture already waited for all silos
        // The AppHostFixture.InitializeAsync() waits for all silos to be healthy
        Assert.NotNull(ApiBaseUrl);
        Assert.NotEmpty(ApiBaseUrl);
    }
}

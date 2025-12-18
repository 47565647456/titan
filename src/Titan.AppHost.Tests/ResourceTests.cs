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
        
        // Verify JSON structure with per-check details
        Assert.Contains("\"status\":", content);
        Assert.Contains("\"checks\":", content);
        Assert.Contains("Healthy", content);
    }
}

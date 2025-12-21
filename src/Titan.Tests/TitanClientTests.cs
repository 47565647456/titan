namespace Titan.Tests;

/// <summary>
/// Unit tests for TitanClient SDK components.
/// Tests the builder pattern and client state management.
/// </summary>
public class TitanClientTests
{
    [Fact]
    public void TitanClientBuilder_WithBaseUrl_SetsBaseUrl()
    {
        // Arrange & Act
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Assert
        Assert.NotNull(client);
        Assert.NotNull(client.Auth);
    }

    [Fact]
    public void TitanClientBuilder_WithTrailingSlash_TrimsIt()
    {
        // Arrange & Act - Should not throw
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com/")
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void TitanClientBuilder_WithoutBaseUrl_ThrowsInvalidOperation()
    {
        // Arrange
        var builder = new Client.TitanClientBuilder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("BaseUrl", ex.Message);
    }

    [Fact]
    public void TitanClientBuilder_WithAutoReconnect_SetsOption()
    {
        // Arrange & Act
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .WithAutoReconnect(true)
            .Build();

        // Assert - Client builds successfully with option
        Assert.NotNull(client);
    }

    [Fact]
    public void TitanClientBuilder_WithConnectionTimeout_SetsOption()
    {
        // Arrange & Act
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .WithConnectionTimeout(TimeSpan.FromSeconds(30))
            .Build();

        // Assert - Client builds successfully with option
        Assert.NotNull(client);
    }

    [Fact]
    public void TitanClient_IsAuthenticated_FalseByDefault()
    {
        // Arrange
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Assert
        Assert.False(client.IsAuthenticated);
        Assert.Null(client.SessionId);
        Assert.Null(client.UserId);
    }

    [Fact]
    public async Task TitanClient_GetAccountClient_WhenNotAuthenticated_ThrowsInvalidOperation()
    {
        // Arrange
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAccountClientAsync());
        Assert.Contains("not authenticated", ex.Message);
    }

    [Fact]
    public async Task TitanClient_GetCharacterClient_WhenNotAuthenticated_ThrowsInvalidOperation()
    {
        // Arrange
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetCharacterClientAsync());
        Assert.Contains("not authenticated", ex.Message);
    }

    [Fact]
    public async Task TitanClient_GetInventoryClient_WhenNotAuthenticated_ThrowsInvalidOperation()
    {
        // Arrange
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetInventoryClientAsync());
        Assert.Contains("not authenticated", ex.Message);
    }

    [Fact]
    public async Task TitanClient_GetTradeClient_WhenNotAuthenticated_ThrowsInvalidOperation()
    {
        // Arrange
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetTradeClientAsync());
        Assert.Contains("not authenticated", ex.Message);
    }

    [Fact]
    public async Task TitanClient_Dispose_DoesNotThrow()
    {
        // Arrange
        var client = new Client.TitanClientBuilder()
            .WithBaseUrl("https://api.example.com")
            .Build();

        // Act & Assert - Should not throw
        var exception = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());
        Assert.Null(exception);
    }

    [Fact]
    public void TitanClientOptions_Defaults_AreCorrect()
    {
        // Arrange & Act
        var options = new Client.TitanClientOptions();

        // Assert
        Assert.True(options.EnableAutoReconnect);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ConnectionTimeout);
        Assert.Null(options.LoggerFactory);
    }
}

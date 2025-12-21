using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for server broadcast functionality.
/// </summary>
[Collection("AppHost")]
public class BroadcastTests : IntegrationTestBase
{
    public BroadcastTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanSendBroadcast_ReturnsMessage()
    {
        // Arrange
        await Fixture.ResetAdminLockoutAsync();
        var client = await CreateAuthenticatedAdminClientAsync();

        var request = new
        {
            Content = "Server maintenance in 5 minutes",
            Type = ServerMessageType.Maintenance,
            Title = "Maintenance Notice",
            DurationSeconds = 30
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/broadcast", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var message = await response.Content.ReadFromJsonAsync<ServerMessage>();
        Assert.NotNull(message);
        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal("Server maintenance in 5 minutes", message.Content);
        Assert.Equal(ServerMessageType.Maintenance, message.Type);
        Assert.Equal("Maintenance Notice", message.Title);
        Assert.Equal(30, message.DurationSeconds);
    }

    [Fact]
    public async Task Admin_CanSendBroadcast_ConnectedUserReceivesMessage()
    {
        // Arrange
        await Fixture.ResetAdminLockoutAsync();
        var client = await CreateAuthenticatedAdminClientAsync();
        
        // Create a user session and connect to broadcast hub
        await using var user = await CreateUserSessionAsync();
        var broadcastHub = await user.GetBroadcastHubAsync();

        // Setup message receiver
        var messageReceived = new TaskCompletionSource<ServerMessage>();
        broadcastHub.On<ServerMessage>("ReceiveServerMessage", message =>
        {
            messageReceived.TrySetResult(message);
        });

        var request = new
        {
            Content = "Achievement unlocked: First Blood!",
            Type = ServerMessageType.Achievement,
            Title = "Achievement",
            IconId = "trophy"
        };

        // Act - Send broadcast
        var response = await client.PostAsJsonAsync("/api/admin/broadcast", request);
        response.EnsureSuccessStatusCode();

        // Wait for message (with timeout)
        ServerMessage? receivedMessage = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            receivedMessage = await messageReceived.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Message not received in time
        }

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("Achievement unlocked: First Blood!", receivedMessage.Content);
        Assert.Equal(ServerMessageType.Achievement, receivedMessage.Type);
        Assert.Equal("Achievement", receivedMessage.Title);
        Assert.Equal("trophy", receivedMessage.IconId);
    }

    [Fact]
    public async Task Unauthenticated_CannotAccessBroadcastApi_ReturnsUnauthorized()
    {
        // Arrange - Create unauthenticated client
        var client = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };

        var request = new
        {
            Content = "This should fail",
            Type = ServerMessageType.Info
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/broadcast", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Broadcast_ValidationRejectsEmptyContent()
    {
        // Arrange
        await Fixture.ResetAdminLockoutAsync();
        var client = await CreateAuthenticatedAdminClientAsync();

        var request = new
        {
            Content = "", // Empty content should fail validation
            Type = ServerMessageType.Info
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/broadcast", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Broadcast_ValidationRejectsLongContent()
    {
        // Arrange
        await Fixture.ResetAdminLockoutAsync();
        var client = await CreateAuthenticatedAdminClientAsync();

        var request = new
        {
            Content = new string('x', 2001), // Exceeds 2000 char limit
            Type = ServerMessageType.Info
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/broadcast", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

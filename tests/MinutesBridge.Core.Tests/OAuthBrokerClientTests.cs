using MinutesBridge.Core.Authentication;

namespace MinutesBridge.Core.Tests;

public sealed class OAuthBrokerClientTests
{
    [Fact]
    public async Task PollAsync_SendsPollingSecretInBodyNotUrl()
    {
        using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
            {
              "status": "pending",
              "accessToken": null,
              "cloudId": null,
              "siteUrl": null,
              "accountDisplayName": null,
              "expiresAtUtc": null
            }
            """));
        using var httpClient = new HttpClient(handler);
        var client = new OAuthBrokerClient(httpClient, new BrokerOptions(new Uri("https://broker.example/")));

        var result = await client.PollAsync("request-123", "top-secret");

        var request = Assert.IsType<HttpRequestMessage>(handler.LastRequest);
        Assert.Equal(BrokerAuthorizationStatus.Pending, result.Status);
        Assert.DoesNotContain("top-secret", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public async Task StartAsync_RejectsAuthorizationRedirectToUnexpectedHost()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
        using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json($$"""
            {
              "requestId": "request-123",
              "pollingSecret": "secret-123",
              "authorizationUrl": "https://attacker.example/sign-in",
              "expiresAtUtc": "{{expires}}",
              "pollIntervalSeconds": 2
            }
            """));
        using var httpClient = new HttpClient(handler);
        var client = new OAuthBrokerClient(httpClient, new BrokerOptions(new Uri("https://broker.example/")));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.StartAsync());
    }

    [Fact]
    public async Task StartAsync_AcceptsBrokerAuthorizationUrl()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
        using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json($$"""
            {
              "requestId": "request-123",
              "pollingSecret": "secret-123",
              "authorizationUrl": "https://broker.example/authorize?id=public-request-id",
              "expiresAtUtc": "{{expires}}",
              "pollIntervalSeconds": 1
            }
            """));
        using var httpClient = new HttpClient(handler);
        var client = new OAuthBrokerClient(httpClient, new BrokerOptions(new Uri("https://broker.example/")));

        var result = await client.StartAsync();

        Assert.Equal(TimeSpan.FromSeconds(2), result.PollInterval);
        Assert.Equal("broker.example", result.AuthorizationUri.Host);
    }
}

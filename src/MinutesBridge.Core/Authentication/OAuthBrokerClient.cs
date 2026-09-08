using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MinutesBridge.Core.Http;

namespace MinutesBridge.Core.Authentication;

public sealed class OAuthBrokerClient(HttpClient httpClient, BrokerOptions options) : IAuthenticationBroker
{
    private const int MaximumResponseBytes = 64 * 1024;

    public async Task<BrokerAuthorizationStart> StartAsync(CancellationToken cancellationToken = default)
    {
        BrokerOptions.Validate(options.BaseUri);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("v1/oauth/atlassian/start"))
        {
            Content = JsonContent.Create(new { client = "MinutesBridge", version = "0.2" })
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var value = await BoundedJsonContent.ReadAsync<StartResponse>(
            response.Content,
            MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);
        ValidateOpaqueValue(value.RequestId, 200, "request ID");
        ValidateOpaqueValue(value.PollingSecret, 512, "polling secret");

        if (!Uri.TryCreate(value.AuthorizationUrl, UriKind.Absolute, out var authorizationUri) ||
            !string.Equals(authorizationUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(authorizationUri.UserInfo) ||
            !string.IsNullOrEmpty(authorizationUri.Fragment) ||
            (!IsSameOrigin(authorizationUri, options.BaseUri) && !IsAtlassianAuthorizationHost(authorizationUri)))
        {
            throw new InvalidDataException("The broker returned an invalid authorization address.");
        }

        var now = DateTimeOffset.UtcNow;
        if (value.ExpiresAtUtc <= now || value.ExpiresAtUtc > now.AddMinutes(30))
        {
            throw new InvalidDataException("The broker returned an invalid authorization expiry.");
        }

        var intervalSeconds = Math.Clamp(value.PollIntervalSeconds, 2, 15);
        return new BrokerAuthorizationStart(
            value.RequestId,
            value.PollingSecret,
            authorizationUri,
            value.ExpiresAtUtc,
            TimeSpan.FromSeconds(intervalSeconds));
    }

    public async Task<BrokerAuthorizationResult> PollAsync(
        string requestId,
        string pollingSecret,
        CancellationToken cancellationToken = default)
    {
        ValidateOpaqueValue(requestId, 200, "request ID");
        ValidateOpaqueValue(pollingSecret, 512, "polling secret");

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("v1/oauth/atlassian/poll"))
        {
            // Secrets stay in the body so they cannot leak through URL logs/history.
            Content = JsonContent.Create(new { requestId, pollingSecret })
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var value = await BoundedJsonContent.ReadAsync<PollResponse>(
            response.Content,
            MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);
        if (!Enum.TryParse<BrokerAuthorizationStatus>(value.Status, true, out var status))
        {
            throw new InvalidDataException("The broker returned an unknown authorization status.");
        }

        if (status != BrokerAuthorizationStatus.Authorized)
        {
            return new BrokerAuthorizationResult(status);
        }

        ValidateOpaqueValue(value.AccessToken, 4096, "access token");
        if (!Guid.TryParse(value.CloudId, out _))
        {
            throw new InvalidDataException("The broker returned an invalid Confluence cloud ID.");
        }

        if (!Uri.TryCreate(value.SiteUrl, UriKind.Absolute, out var siteUri) ||
            !IsAtlassianCloudSite(siteUri))
        {
            throw new InvalidDataException("The broker returned an invalid Confluence site address.");
        }

        var session = new BrokerSession(
            value.AccessToken!,
            value.CloudId!,
            siteUri,
            value.AccountDisplayName?.Trim() ?? string.Empty,
            value.ExpiresAtUtc ?? throw new InvalidDataException("The broker omitted the session expiry."));
        if (!session.IsUsable(DateTimeOffset.UtcNow))
        {
            throw new InvalidDataException("The broker returned an expired session.");
        }

        return new BrokerAuthorizationResult(status, session);
    }

    private Uri BuildUri(string relativePath) => new(options.BaseUri, relativePath);

    private static bool IsSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static bool IsAtlassianAuthorizationHost(Uri uri) =>
        string.Equals(uri.Host, "auth.atlassian.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort;

    private static bool IsAtlassianCloudSite(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        uri.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase);

    private static void ValidateOpaqueValue(string? value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
            value.Any(char.IsControl))
        {
            throw new InvalidDataException($"The broker returned an invalid {name}.");
        }
    }

    private sealed record StartResponse(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("pollingSecret")] string PollingSecret,
        [property: JsonPropertyName("authorizationUrl")] string AuthorizationUrl,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
        [property: JsonPropertyName("pollIntervalSeconds")] int PollIntervalSeconds);

    private sealed record PollResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("cloudId")] string? CloudId,
        [property: JsonPropertyName("siteUrl")] string? SiteUrl,
        [property: JsonPropertyName("accountDisplayName")] string? AccountDisplayName,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset? ExpiresAtUtc);
}

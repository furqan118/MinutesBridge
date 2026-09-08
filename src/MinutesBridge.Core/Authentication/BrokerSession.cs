namespace MinutesBridge.Core.Authentication;

public sealed record BrokerSession(
    string AccessToken,
    string CloudId,
    Uri SiteUri,
    string AccountDisplayName,
    DateTimeOffset ExpiresAtUtc)
{
    public bool IsUsable(DateTimeOffset nowUtc) =>
        !string.IsNullOrWhiteSpace(AccessToken) && ExpiresAtUtc > nowUtc.AddMinutes(1);

    public override string ToString() =>
        $"BrokerSession(CloudId={CloudId}, Site={SiteUri.Host}, ExpiresAtUtc={ExpiresAtUtc:O})";
}

public enum BrokerAuthorizationStatus
{
    Pending,
    Authorized,
    Declined,
    Expired
}

public sealed record BrokerAuthorizationStart(
    string RequestId,
    string PollingSecret,
    Uri AuthorizationUri,
    DateTimeOffset ExpiresAtUtc,
    TimeSpan PollInterval)
{
    public override string ToString() =>
        $"BrokerAuthorizationStart(RequestId=[redacted], AuthorizationHost={AuthorizationUri.Host}, ExpiresAtUtc={ExpiresAtUtc:O})";
}

public sealed record BrokerAuthorizationResult(
    BrokerAuthorizationStatus Status,
    BrokerSession? Session = null);

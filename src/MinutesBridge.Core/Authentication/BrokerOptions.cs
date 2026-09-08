namespace MinutesBridge.Core.Authentication;

public sealed record BrokerOptions(Uri BaseUri)
{
    public static BrokerOptions FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("MINUTESBRIDGE_BROKER_BASE_URI");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "Set MINUTESBRIDGE_BROKER_BASE_URI to the HTTPS address supplied by your administrator.");
        }

        Validate(uri);
        return new BrokerOptions(uri);
    }

    public static void Validate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "The OAuth broker address must be an HTTPS origin without a path, credentials, query text, or a fragment.",
                nameof(uri));
        }
    }
}

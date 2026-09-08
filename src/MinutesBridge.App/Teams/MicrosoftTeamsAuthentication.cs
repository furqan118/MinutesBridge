using Microsoft.Identity.Client;

namespace MinutesBridge.App.Teams;

internal sealed class MicrosoftTeamsAuthentication
{
    private static readonly string[] Scopes = ["Chat.Read"];
    private readonly IPublicClientApplication _application;

    public MicrosoftTeamsAuthentication()
    {
        var clientId = ReadGuid("MINUTESBRIDGE_MICROSOFT_CLIENT_ID", "Microsoft application client ID");
        var tenantId = ReadGuid("MINUTESBRIDGE_MICROSOFT_TENANT_ID", "Microsoft tenant ID");
        _application = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithDefaultRedirectUri()
            .Build();
    }

    public async Task<AuthenticationResult> AcquireAsync(IntPtr parentWindow, CancellationToken cancellationToken)
    {
        var accounts = await _application.GetAccountsAsync().ConfigureAwait(true);
        var account = accounts.FirstOrDefault();
        if (account is not null)
        {
            try
            {
                return await _application.AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (MsalUiRequiredException)
            {
                // Continue with an explicit system-browser sign-in.
            }
        }

        return await _application.AcquireTokenInteractive(Scopes)
            .WithParentActivityOrWindow(parentWindow)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(true);
    }

    private static string ReadGuid(string variableName, string displayName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!Guid.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Configure the {displayName} before connecting to Teams.");
        }

        return parsed.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
    }
}

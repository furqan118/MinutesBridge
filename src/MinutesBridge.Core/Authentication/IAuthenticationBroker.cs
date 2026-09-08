namespace MinutesBridge.Core.Authentication;

public interface IAuthenticationBroker
{
    Task<BrokerAuthorizationStart> StartAsync(CancellationToken cancellationToken = default);

    Task<BrokerAuthorizationResult> PollAsync(
        string requestId,
        string pollingSecret,
        CancellationToken cancellationToken = default);
}

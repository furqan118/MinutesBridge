namespace MinutesBridge.Core.Authentication;

public interface ISessionStore
{
    void Save(BrokerSession session);

    BrokerSession? TryLoad();

    void Delete();
}

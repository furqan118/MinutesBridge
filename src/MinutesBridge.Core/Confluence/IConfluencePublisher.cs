namespace MinutesBridge.Core.Confluence;

public interface IConfluencePublisher
{
    Task<PublishedPage> CreatePageAsync(
        ConfluencePageRequest page,
        string accessToken,
        CancellationToken cancellationToken = default);
}

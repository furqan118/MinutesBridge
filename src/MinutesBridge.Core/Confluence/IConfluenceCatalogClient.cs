namespace MinutesBridge.Core.Confluence;

public interface IConfluenceCatalogClient
{
    Task<IReadOnlyList<ConfluenceSpace>> GetSpacesAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConfluencePageSummary>> GetRootPagesAsync(
        string accessToken,
        string spaceId,
        Uri siteUri,
        CancellationToken cancellationToken = default);

    Task<ConfluencePageSummary?> FindLatestMeetingPageAsync(
        string accessToken,
        string spaceId,
        string group,
        DateOnly beforeDate,
        Uri siteUri,
        CancellationToken cancellationToken = default);
}

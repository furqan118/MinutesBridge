namespace MinutesBridge.Core.Confluence;

public sealed record ConfluencePageRequest(
    string SpaceId,
    string ParentPageId,
    string Title,
    string StorageBody,
    bool SaveAsDraft);

public sealed record PublishedPage(string Id, Uri WebUrl);

namespace MinutesBridge.Core.Confluence;

public sealed record ConfluenceSpace(string Id, string Key, string Name)
{
    public override string ToString() => $"{Name} ({Key})";
}

public sealed record ConfluencePageSummary(string Id, string Title, Uri? WebUri)
{
    public override string ToString() => Title;
}

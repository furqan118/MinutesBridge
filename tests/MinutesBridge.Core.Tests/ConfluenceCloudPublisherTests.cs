using MinutesBridge.Core.Confluence;

namespace MinutesBridge.Core.Tests;

public sealed class ConfluenceCloudPublisherTests
{
    [Theory]
    [InlineData("../space", "123")]
    [InlineData("123", "parent/child")]
    public async Task CreatePageAsync_RejectsInvalidDestinationIdentifiers(string spaceId, string parentId)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        using var httpClient = new HttpClient(handler);
        var publisher = new ConfluenceCloudPublisher(httpClient, "9fe25af9-2a43-4fd1-889c-52d68f254936");
        var page = new ConfluencePageRequest(spaceId, parentId, "Title", "<p>Safe</p>", true);

        await Assert.ThrowsAsync<ArgumentException>(() => publisher.CreatePageAsync(page, "access-token"));
        Assert.Null(handler.LastRequest);
    }
}

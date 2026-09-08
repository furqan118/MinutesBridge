using System.Net;
using MinutesBridge.Core.Teams;

namespace MinutesBridge.Core.Tests;

public sealed class TeamsGraphClientTests
{
    [Fact]
    public async Task GetMeetingChatsAsyncReturnsOnlyBoundedMeetingChats()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
            {
              "value": [
                {"id":"19:meeting_bhits@thread.v2","topic":"BHITS Weekly","chatType":"meeting","lastUpdatedDateTime":"2026-09-08T15:00:00Z"},
                {"id":"19:group@thread.v2","topic":"BHITS chat","chatType":"group","lastUpdatedDateTime":"2026-09-08T16:00:00Z"},
                {"id":"19:meeting_other@thread.v2","topic":"Other Meeting","chatType":"meeting","lastUpdatedDateTime":"2026-09-07T15:00:00Z"}
              ]
            }
            """));
        using var httpClient = new HttpClient(handler);
        var client = new TeamsGraphClient(httpClient);

        var chats = await client.GetMeetingChatsAsync("safe-token");

        Assert.Equal(2, chats.Count);
        Assert.Equal("BHITS Weekly", chats[0].Topic);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("safe-token", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.DoesNotContain("safe-token", handler.LastRequest.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRecentMessagesAsyncEncodesOpaqueChatIdentifier()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
            {
              "value": [{
                "id":"message-1",
                "createdDateTime":"2026-09-08T15:00:00Z",
                "body":{"contentType":"html","content":"<p>Meeting notes</p>"}
              }]
            }
            """));
        using var httpClient = new HttpClient(handler);
        var client = new TeamsGraphClient(httpClient);

        var messages = await client.GetRecentMessagesAsync("safe-token", "19:meeting/value@thread.v2");

        Assert.Single(messages);
        Assert.Contains("19%3Ameeting%2Fvalue%40thread.v2", handler.LastRequest!.RequestUri!.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("safe-token", handler.LastRequest.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }
}

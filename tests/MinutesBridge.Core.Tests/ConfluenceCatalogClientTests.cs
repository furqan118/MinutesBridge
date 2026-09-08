using System.Net.Http.Headers;
using MinutesBridge.Core.Confluence;

namespace MinutesBridge.Core.Tests;

public sealed class ConfluenceCatalogClientTests
{
    private const string CloudId = "9fe25af9-2a43-4fd1-889c-52d68f254936";

    [Fact]
    public async Task GetSpacesAsyncReturnsOnlyValidAuthorizedResults()
    {
        using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
            {"results":[
              {"id":"42","key":"BHITS","name":"BHITS"},
              {"id":"../bad","key":"BAD","name":"Unsafe"}
            ]}
            """));
        using var httpClient = new HttpClient(handler);
        var client = new ConfluenceCatalogClient(httpClient, CloudId);

        var spaces = await client.GetSpacesAsync("access-token");

        var request = Assert.IsType<HttpRequestMessage>(handler.LastRequest);
        var space = Assert.Single(spaces);
        Assert.Equal("42", space.Id);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), request.Headers.Authorization);
        Assert.Equal("api.atlassian.com", request.RequestUri!.Host);
    }

    [Fact]
    public async Task FindLatestMeetingPageAsyncUsesTitleDateAndSameSiteLinks()
    {
        using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
            {"results":[
              {"id":"10","title":"2026-08-01 BHITS/Meeting Notes","_links":{"webui":"/wiki/spaces/BHITS/pages/10"}},
              {"id":"11","title":"2026-09-06 BHITS/Meeting Notes","_links":{"webui":"/wiki/spaces/BHITS/pages/11"}},
              {"id":"13","title":"2026-09-09 BHITS/Meeting Notes","_links":{"webui":"/wiki/spaces/BHITS/pages/13"}},
              {"id":"12","title":"2026-10-01 Other/Meeting Notes","_links":{"webui":"https://attacker.example/page"}}
            ]}
            """));
        using var httpClient = new HttpClient(handler);
        var client = new ConfluenceCatalogClient(httpClient, CloudId);

        var page = await client.FindLatestMeetingPageAsync(
            "access-token",
            "42",
            "BHITS",
            new DateOnly(2026, 9, 8),
            new Uri("https://bhits.atlassian.net/"));

        Assert.NotNull(page);
        Assert.Equal("11", page.Id);
        Assert.Equal("https://bhits.atlassian.net/wiki/spaces/BHITS/pages/11", page.WebUri!.AbsoluteUri);
    }
}

using MinutesBridge.Core.Confluence;
using MinutesBridge.Core.Models;

namespace MinutesBridge.Core.Tests;

public sealed class ConfluenceStorageRendererTests
{
    [Fact]
    public void Render_EncodesUntrustedMeetingContent()
    {
        var meeting = new MeetingMinutes(
            "BHITS", new DateOnly(2026, 9, 6), "10:00", "Teams", "A < B", "Furqan",
            ["Alice & Bob"], [], [new AgendaItem("Security", "Alice", ["<script>alert(1)</script>"])]);

        var html = new ConfluenceStorageRenderer().Render(meeting);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Alice &amp; Bob", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PageTitle_UsesSortableIsoDate()
    {
        var meeting = new MeetingMinutes(
            "BHITS", new DateOnly(2026, 9, 6), "", "Teams", "", "", [], [], []);

        Assert.Equal("2026-09-06 BHITS/Meeting Notes", meeting.PageTitle);
    }
}

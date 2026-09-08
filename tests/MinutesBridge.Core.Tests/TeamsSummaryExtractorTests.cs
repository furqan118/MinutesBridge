using MinutesBridge.Core.Teams;

namespace MinutesBridge.Core.Tests;

public sealed class TeamsSummaryExtractorTests
{
    [Fact]
    public void ExtractFindsExactHeadingAndPreservesListStructure()
    {
        var message = new TeamsChatMessage(
            "message-1",
            $"<p>Earlier text</p><p><strong>{TeamsSummaryExtractor.RequiredHeading}</strong></p><ul><li>First item</li><li>Second &amp; final item</li></ul>",
            "html",
            new DateTimeOffset(2026, 9, 8, 15, 30, 0, TimeSpan.Zero));

        var result = TeamsSummaryExtractor.Extract("chat-1", [message]);

        Assert.StartsWith(TeamsSummaryExtractor.RequiredHeading, result.Content, StringComparison.Ordinal);
        Assert.Contains("• First item", result.Content, StringComparison.Ordinal);
        Assert.Contains("• Second & final item", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Earlier text", result.Content, StringComparison.Ordinal);
        Assert.Equal("message-1", result.MessageId);
    }

    [Fact]
    public void ExtractRejectsMessagesWithoutRequiredHeading()
    {
        var message = new TeamsChatMessage(
            "message-1",
            "<p>A different summary</p>",
            "html",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidDataException>(() => TeamsSummaryExtractor.Extract("chat-1", [message]));
    }
}

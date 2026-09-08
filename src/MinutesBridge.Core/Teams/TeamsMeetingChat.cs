using System.Globalization;

namespace MinutesBridge.Core.Teams;

public sealed record TeamsMeetingChat(
    string Id,
    string Topic,
    DateTimeOffset LastUpdatedDateTime)
{
    public override string ToString() => string.Create(
        CultureInfo.CurrentCulture,
        $"{Topic} ({LastUpdatedDateTime.LocalDateTime:g})");
}

public sealed record TeamsChatMessage(
    string Id,
    string BodyContent,
    string BodyContentType,
    DateTimeOffset CreatedDateTime);

public sealed record TeamsImportedSummary(
    string ChatId,
    string MessageId,
    DateOnly MeetingDate,
    string Content);

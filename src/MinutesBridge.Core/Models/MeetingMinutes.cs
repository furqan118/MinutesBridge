namespace MinutesBridge.Core.Models;

public sealed record AgendaItem(string Topic, string Name, IReadOnlyList<string> Notes);

public sealed record MeetingMinutes(
    string Group,
    DateOnly Date,
    string Time,
    string Place,
    string Facilitator,
    string NoteTaker,
    IReadOnlyList<string> Attendees,
    IReadOnlyList<string> Regrets,
    IReadOnlyList<AgendaItem> Agenda,
    Uri? PreviousMinutes = null)
{
    public string PageTitle => $"{Date:yyyy-MM-dd} {Group.Trim()}/Meeting Notes";
}

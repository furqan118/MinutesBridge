using MinutesBridge.Core.Editing;
using MinutesBridge.Core.Models;

namespace MinutesBridge.App.Editing;

internal sealed class EditableAgendaRow
{
    public string Topic { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public AgendaDraftRow ToDraft() => new(Topic, Name, Notes);

    public static EditableAgendaRow FromAgendaItem(AgendaItem item) => new()
    {
        Topic = item.Topic,
        Name = item.Name,
        Notes = string.Join(Environment.NewLine, item.Notes)
    };
}

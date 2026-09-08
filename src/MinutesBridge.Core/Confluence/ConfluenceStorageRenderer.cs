using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using MinutesBridge.Core.Models;

namespace MinutesBridge.Core.Confluence;

public sealed class ConfluenceStorageRenderer
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preserve the existing public instance API.")]
    public string Render(MeetingMinutes meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        var html = new StringBuilder();

        html.Append("<p><strong>Place:</strong> ").Append(E(meeting.Place)).Append("</p>")
            .Append("<p><strong>Time:</strong> ").Append(E(meeting.Time)).Append("</p>")
            .Append("<p><strong>Facilitator:</strong> ").Append(E(meeting.Facilitator)).Append("</p>")
            .Append("<p><strong>Note Taker:</strong> ").Append(E(meeting.NoteTaker)).Append("</p>");

        if (meeting.PreviousMinutes is not null)
        {
            html.Append("<p><strong>Please read:</strong> <a href=\"")
                .Append(E(meeting.PreviousMinutes.AbsoluteUri))
                .Append("\">Previous meeting notes</a></p>");
        }

        AppendPeople(html, "Attendees", meeting.Attendees);
        AppendPeople(html, "Regrets", meeting.Regrets);

        html.Append("<h2>AGENDA/NOTE</h2>")
            .Append("<table><tbody><tr><th>Topic</th><th>Name</th><th>Notes</th></tr>");

        foreach (var item in meeting.Agenda)
        {
            html.Append("<tr><td>").Append(E(item.Topic)).Append("</td><td>")
                .Append(E(item.Name)).Append("</td><td><ul>");
            foreach (var note in item.Notes)
            {
                html.Append("<li>").Append(E(note)).Append("</li>");
            }
            html.Append("</ul></td></tr>");
        }

        return html.Append("</tbody></table>").ToString();
    }

    private static void AppendPeople(StringBuilder html, string heading, IReadOnlyList<string> people)
    {
        html.Append("<p><strong>").Append(heading).Append(":</strong></p><ul>");
        foreach (var person in people)
        {
            html.Append("<li>").Append(E(person)).Append("</li>");
        }
        html.Append("</ul>");
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value?.Trim() ?? string.Empty);
}

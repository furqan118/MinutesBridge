using System.Net;
using System.Text;

namespace MinutesBridge.Core.Teams;

public static class TeamsSummaryExtractor
{
    public const int MaximumMessageCharacters = 200_000;
    public const string RequiredHeading = "Everyone—that’s a wrap. Here’s the complete rundown of today’s meeting.";

    public static TeamsImportedSummary Extract(string chatId, IEnumerable<TeamsChatMessage> messages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages.OrderByDescending(item => item.CreatedDateTime))
        {
            if (message.BodyContent.Length > MaximumMessageCharacters)
            {
                continue;
            }

            var plainText = string.Equals(message.BodyContentType, "html", StringComparison.OrdinalIgnoreCase)
                ? HtmlToPlainText(message.BodyContent)
                : NormalizeLines(message.BodyContent);
            var headingIndex = plainText.IndexOf(RequiredHeading, StringComparison.Ordinal);
            if (headingIndex < 0)
            {
                continue;
            }

            var content = plainText[headingIndex..].Trim();
            if (content.Length == 0 || content.Length > MaximumMessageCharacters)
            {
                continue;
            }

            return new TeamsImportedSummary(
                chatId,
                message.Id,
                DateOnly.FromDateTime(message.CreatedDateTime.LocalDateTime),
                content);
        }

        throw new InvalidDataException(
            $"No recent Teams message begins with the required heading: {RequiredHeading}");
    }

    private static string HtmlToPlainText(string html)
    {
        var text = new StringBuilder(Math.Min(html.Length, MaximumMessageCharacters));
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] != '<')
            {
                text.Append(html[index]);
                index++;
                continue;
            }

            var close = html.IndexOf('>', index + 1);
            if (close < 0)
            {
                text.Append(html.AsSpan(index));
                break;
            }

            var tag = ReadTagName(html.AsSpan(index + 1, close - index - 1), out var closing);
            if (string.Equals(tag, "li", StringComparison.OrdinalIgnoreCase) && !closing)
            {
                AppendNewLine(text);
                text.Append("• ");
            }
            else if (string.Equals(tag, "br", StringComparison.OrdinalIgnoreCase) ||
                     (closing && IsBlockTag(tag)))
            {
                AppendNewLine(text);
            }

            index = close + 1;
        }

        return NormalizeLines(WebUtility.HtmlDecode(text.ToString()));
    }

    private static string ReadTagName(ReadOnlySpan<char> tag, out bool closing)
    {
        tag = tag.Trim();
        closing = tag.Length > 0 && tag[0] == '/';
        if (closing)
        {
            tag = tag[1..].TrimStart();
        }

        var length = 0;
        while (length < tag.Length && char.IsAsciiLetterOrDigit(tag[length]))
        {
            length++;
        }

        return tag[..length].ToString();
    }

    private static bool IsBlockTag(string tag) =>
        tag.Equals("p", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("div", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("li", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("ul", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("ol", StringComparison.OrdinalIgnoreCase) ||
        (tag.Length == 2 && (tag[0] == 'h' || tag[0] == 'H') && tag[1] is >= '1' and <= '6');

    private static void AppendNewLine(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != '\n')
        {
            text.AppendLine();
        }
    }

    private static string NormalizeLines(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .ToArray();
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (line.Length == 0 && (output.Count == 0 || output[^1].Length == 0))
            {
                continue;
            }

            output.Add(line);
        }

        return string.Join(Environment.NewLine, output).Trim();
    }
}

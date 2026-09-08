using System.Diagnostics.CodeAnalysis;
using MinutesBridge.Core.Models;

namespace MinutesBridge.Core.Parsing;

public sealed class FacilitatorNotesParser
{
    public const int MaximumInputCharacters = 100_000;
    public const int MaximumLines = 2_000;
    public const int MaximumLineCharacters = 2_000;
    private static readonly char[] BulletPrefixes = ['-', '*', '•'];

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preserve the existing public instance API.")]
    public IReadOnlyList<AgendaItem> Parse(string pastedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastedText);
        if (pastedText.Length > MaximumInputCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pastedText),
                $"Meeting notes must be {MaximumInputCharacters:N0} characters or fewer.");
        }

        var lines = pastedText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length > MaximumLines || lines.Any(line => line.Length > MaximumLineCharacters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pastedText),
                "Meeting notes contain too many lines or an individual line is too long.");
        }

        if (lines.Length == 0)
        {
            return [];
        }

        var results = new List<AgendaItem>();
        string? topic = null;
        var notes = new List<string>();

        foreach (var line in lines)
        {
            if (IsHeading(line))
            {
                Flush();
                topic = NormalizeHeading(line);
                continue;
            }

            notes.Add(RemoveBullet(line));
        }

        Flush();

        if (results.Count == 0)
        {
            results.Add(new AgendaItem("Meeting notes", string.Empty, lines.Select(RemoveBullet).ToArray()));
        }

        return results;

        void Flush()
        {
            if (topic is null && notes.Count == 0)
            {
                return;
            }

            results.Add(new AgendaItem(topic ?? "Meeting notes", string.Empty, notes.ToArray()));
            topic = null;
            notes.Clear();
        }
    }

    private static bool IsHeading(string line)
    {
        if (line.Length > 100 || BulletPrefixes.Contains(line[0]))
        {
            return false;
        }

        // Plain clipboard text loses most rich-text semantics. Only treat explicit
        // Markdown headings or colon-terminated lines as topics; guessing from a
        // short sentence risks silently changing the meaning of meeting notes.
        return line.StartsWith("## ", StringComparison.Ordinal) || line.EndsWith(':');
    }

    private static string NormalizeHeading(string line) =>
        line.TrimStart('#', ' ').TrimEnd(':').Trim();

    private static string RemoveBullet(string line) =>
        line.TrimStart().TrimStart(BulletPrefixes).TrimStart();
}

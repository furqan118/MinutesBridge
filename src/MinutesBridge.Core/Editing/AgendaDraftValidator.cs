using MinutesBridge.Core.Models;

namespace MinutesBridge.Core.Editing;

public static class AgendaDraftValidator
{
    public const int MaximumRows = 250;
    public const int MaximumTopicCharacters = 200;
    public const int MaximumNameCharacters = 200;
    public const int MaximumNotesPerRow = 250;
    public const int MaximumNoteCharacters = 2_000;
    public const int MaximumTotalCharacters = 100_000;

    public static IReadOnlyList<AgendaItem> ValidateAndConvert(IEnumerable<AgendaDraftRow> draftRows)
    {
        ArgumentNullException.ThrowIfNull(draftRows);

        var agenda = new List<AgendaItem>();
        var totalCharacters = 0;

        foreach (var row in draftRows)
        {
            if (agenda.Count >= MaximumRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(draftRows),
                    $"The agenda can contain at most {MaximumRows} rows.");
            }

            ArgumentNullException.ThrowIfNull(row);
            var topic = RequiredSingleLine(row.Topic, "agenda topic", MaximumTopicCharacters);
            var name = OptionalSingleLine(row.Name, "agenda name", MaximumNameCharacters);
            var notes = SplitNotes(row.Notes);

            totalCharacters += topic.Length + name.Length + notes.Sum(note => note.Length);
            if (totalCharacters > MaximumTotalCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(draftRows),
                    $"The editable agenda must be {MaximumTotalCharacters:N0} characters or fewer.");
            }

            agenda.Add(new AgendaItem(topic, name, notes));
        }

        if (agenda.Count == 0)
        {
            throw new ArgumentException("Add at least one agenda row before generating the preview.", nameof(draftRows));
        }

        return agenda;
    }

    private static string[] SplitNotes(string value)
    {
        var notes = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (notes.Length > MaximumNotesPerRow)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"An agenda row can contain at most {MaximumNotesPerRow} notes.");
        }

        if (notes.Any(note => note.Length > MaximumNoteCharacters || note.Any(char.IsControl)))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An agenda note is invalid or too long.");
        }

        return notes;
    }

    private static string RequiredSingleLine(string value, string fieldName, int maximumLength)
    {
        var result = OptionalSingleLine(value, fieldName, maximumLength);
        if (result.Length == 0)
        {
            throw new ArgumentException("Enter a topic for every agenda row.", fieldName);
        }

        return result;
    }

    private static string OptionalSingleLine(string value, string fieldName, int maximumLength)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length > maximumLength || result.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(fieldName, $"The {fieldName} is invalid or too long.");
        }

        return result;
    }
}

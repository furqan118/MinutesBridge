using MinutesBridge.Core.Editing;

namespace MinutesBridge.Core.Tests;

public sealed class AgendaDraftValidatorTests
{
    [Fact]
    public void ValidateAndConvertTrimsRowsAndSplitsNotes()
    {
        var rows = new[]
        {
            new AgendaDraftRow(" Security ", " Alice ", " First item\r\n\r\n Second item ")
        };

        var agenda = AgendaDraftValidator.ValidateAndConvert(rows);

        var item = Assert.Single(agenda);
        Assert.Equal("Security", item.Topic);
        Assert.Equal("Alice", item.Name);
        Assert.Equal(["First item", "Second item"], item.Notes);
    }

    [Fact]
    public void ValidateAndConvertRejectsMissingTopic()
    {
        var rows = new[] { new AgendaDraftRow(" ", "", "A note") };

        var exception = Assert.Throws<ArgumentException>(() => AgendaDraftValidator.ValidateAndConvert(rows));

        Assert.Contains("topic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndConvertRejectsTooManyRows()
    {
        var rows = Enumerable.Range(0, AgendaDraftValidator.MaximumRows + 1)
            .Select(index => new AgendaDraftRow($"Topic {index}", "", "A note"));

        Assert.Throws<ArgumentOutOfRangeException>(() => AgendaDraftValidator.ValidateAndConvert(rows));
    }

    [Fact]
    public void ValidateAndConvertRejectsControlCharacters()
    {
        var rows = new[] { new AgendaDraftRow("Security", "", "Safe\tunsafe") };

        Assert.Throws<ArgumentOutOfRangeException>(() => AgendaDraftValidator.ValidateAndConvert(rows));
    }
}

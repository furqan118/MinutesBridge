using MinutesBridge.Core.Parsing;

namespace MinutesBridge.Core.Tests;

public sealed class FacilitatorNotesParserTests
{
    [Fact]
    public void ParseGroupsBulletsUnderTopics()
    {
        const string input = "ACE version control:\n- Confirm release owner.\n- Review versions.\n\nStaff events:\n• Confirm date.";

        var items = new FacilitatorNotesParser().Parse(input);

        Assert.Equal(2, items.Count);
        Assert.Equal("ACE version control", items[0].Topic);
        Assert.Equal(2, items[0].Notes.Count);
        Assert.Equal("Staff events", items[1].Topic);
    }

    [Fact]
    public void ParseRejectsOversizedNotes()
    {
        var input = new string('a', FacilitatorNotesParser.MaximumInputCharacters + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FacilitatorNotesParser().Parse(input));
    }
}

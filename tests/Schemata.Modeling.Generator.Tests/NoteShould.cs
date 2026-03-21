using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class NoteShould
{
    [Theory]
    [InlineData("Note 'hello'", "hello")]
    [InlineData("Note \"world\"", "world")]
    [InlineData("note 'case insensitive'", "case insensitive")]
    [InlineData("NOTE 'upper'", "upper")]
    public void ParseNote(string input, string expected) {
        var result = Parser.Note.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void ParseMultilineNote() {
        var result = Parser.Note.Parse("Note '''multi\nline'''");
        Assert.NotNull(result);
        Assert.Equal("multi\nline", result.Text);
    }

    [Fact]
    public void ParseTripleDoubleNote() {
        var input  = "Note \"\"\"triple\ndouble\"\"\"";
        var result = Parser.Note.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("triple\ndouble", result.Text);
    }
}

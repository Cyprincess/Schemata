using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests;

public class TestSimpleTerms
{
    [Theory]
    [InlineData("namespace Schemata", "Schemata")]
    [InlineData("namespace Schemata.DSL", "Schemata.DSL")]
    [InlineData("namespace Schemata.DSL.System", "Schemata.DSL.System")]
    public void ShouldParseNamespace(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Namespace.Parse(mark, scanner);
        Assert.Equal(expected, term?.Name);
    }

    [Theory]
    [InlineData("use Identifier", new[] { "Identifier" })]
    [InlineData("use Identifier, Timestamp", new[] { "Identifier", "Timestamp" })]
    [InlineData("use Identifier, Timestamp, Concurrency", new[] { "Identifier", "Timestamp", "Concurrency" })]
    [InlineData("use Schemata.DSL.Identifier", new[] { "Schemata.DSL.Identifier" })]
    public void ShouldParseUse(string syntax, string[] expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var terms   = Use.Parse(mark, scanner);
        Assert.Equal(expected, terms?.Select(t => t.Name));
    }

    [Theory]
    [InlineData("\"Hello World\"", "Hello World")]
    [InlineData("'Hello World'", "Hello World")]
    [InlineData("Hello World", "Hello")]
    [InlineData("'''Hello\nWorld'''", "Hello\nWorld")]
    public void ShouldParseValue(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Value.Parse(mark, scanner);
        Assert.Equal(expected, term.Body);
    }

    [Fact]
    public void ShouldParseNote() {
        var mark    = new Mark();
        var scanner = new Scanner("note \"Hello World\"");
        var term    = Note.Parse(mark, scanner);
        Assert.Equal("Hello World", term?.Comment);
    }

    [Theory]
    [InlineData("Default NULL", "Default=NULL")]
    [InlineData("Default \"Hello\"", "Default=Hello")]
    [InlineData("Length 8", "Length=8")]
    [InlineData("Algorithm Hash.SHA256", "Algorithm=Hash.SHA256")]
    public void ShouldParseProperty(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Property.Parse(mark, scanner);
        Assert.Equal(expected, $"{term?.Name}={term?.Body}");
    }
}

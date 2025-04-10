using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class SimpleTermsShould
{
    [Theory]
    [InlineData("namespace Schemata", "Schemata")]
    [InlineData("namespace Schemata.DSL", "Schemata.DSL")]
    [InlineData("namespace Schemata.DSL.System", "Schemata.DSL.System")]
    public void ParseNamespace_WithValidSyntax_ReturnsCorrectExpression(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Namespace.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(expected, term.Name);
    }

    [Theory]
    [InlineData("use Identifier", new[] { "Identifier" })]
    [InlineData("use Identifier, Timestamp", new[] { "Identifier", "Timestamp" })]
    [InlineData("use Identifier, Timestamp, Concurrency", new[] { "Identifier", "Timestamp", "Concurrency" })]
    [InlineData("use Schemata.DSL.Identifier", new[] { "Schemata.DSL.Identifier" })]
    public void ParseUse_WithValidSyntax_ReturnsCorrectExpression(string syntax, string[] expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var terms   = Use.Parse(mark, scanner);

        Assert.NotNull(terms);
        Assert.Equal(expected, terms.Select(t => t.Name).ToArray());
    }

    [Theory]
    [InlineData("\"Hello World\"", "Hello World")]
    [InlineData("'Hello World'", "Hello World")]
    [InlineData("Hello World", "Hello")]
    [InlineData("'''Hello\nWorld'''", "Hello\nWorld")]
    public void ParseValue_WithValidSyntax_ReturnsCorrectExpression(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Value.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(expected, term.Body);
    }

    [Fact]
    public void ParseNote_WithValidSyntax_ReturnsCorrectExpression() {
        var mark    = new Mark();
        var scanner = new Scanner("note \"Hello World\"");
        var term    = Note.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal("Hello World", term.Comment);
    }

    [Theory]
    [InlineData(SkmConstants.Options.AutoIncrement, SkmConstants.Options.AutoIncrement)]
    [InlineData("auto_increment", SkmConstants.Options.AutoIncrement)]
    [InlineData("auto increment", SkmConstants.Options.AutoIncrement)]
    [InlineData(SkmConstants.Options.PrimaryKey, SkmConstants.Options.PrimaryKey)]
    [InlineData("primary_key", SkmConstants.Options.PrimaryKey)]
    [InlineData("primary key", SkmConstants.Options.PrimaryKey)]
    [InlineData(SkmConstants.Options.NotNull, SkmConstants.Options.Required)]
    [InlineData("not_null", SkmConstants.Options.Required)]
    [InlineData("not null", SkmConstants.Options.Required)]
    [InlineData(SkmConstants.Options.Required, SkmConstants.Options.Required)]
    [InlineData(SkmConstants.Options.Unique, SkmConstants.Options.Unique)]
    [InlineData(SkmConstants.Options.BTree, SkmConstants.Options.BTree)]
    [InlineData("b_tree", SkmConstants.Options.BTree)]
    [InlineData("b tree", SkmConstants.Options.BTree)]
    [InlineData(SkmConstants.Options.Hash, SkmConstants.Options.Hash)]
    [InlineData(SkmConstants.Options.Omit, SkmConstants.Options.Omit)]
    [InlineData(SkmConstants.Options.OmitAll, SkmConstants.Options.OmitAll)]
    [InlineData("omit_all", SkmConstants.Options.OmitAll)]
    [InlineData("omit all", SkmConstants.Options.OmitAll)]
    public void ParseOption_WithValidSyntax_ReturnsCorrectExpression(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Option.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(expected, term.Name);
    }

    [Theory]
    [InlineData("Default NULL", "Default=NULL")]
    [InlineData("Default \"Hello\"", "Default=Hello")]
    [InlineData("Length 8", "Length=8")]
    [InlineData("Algorithm Hash.SHA256", "Algorithm=Hash.SHA256")]
    public void ParseProperty_WithValidSyntax_ReturnsCorrectExpression(string syntax, string expected) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Property.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(expected, $"{term.Name}={term.Body}");
    }
}

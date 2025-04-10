using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class EnumShould
{
    [Theory]
    [InlineData("Foo", "Foo", "Foo")]
    [InlineData("Foo { Note FOOBAR }", "Foo", "Foo", "FOOBAR")]
    [InlineData("Name=Value", "Name", "Value")]
    [InlineData("Name=Value{Note FOOBAR}", "Name", "Value", "FOOBAR")]
    [InlineData("Name = Value", "Name", "Value")]
    [InlineData("Name = Value { Note FOOBAR }", "Name", "Value", "FOOBAR")]
    public void ParseEnumValue_WithValidSyntax_ReturnsCorrectExpression(
        string  syntax,
        string  name,
        string? body,
        string? note = null) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = EnumValue.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(name, term.Name);
        Assert.Equal(body, term.Body);
        Assert.Equal(note, term.Note?.Comment);
    }

    [Theory]
    [InlineData("enum Foo {Bar}", "Foo", new[] { "Bar" })]
    [InlineData("""
                enum Foo {
                Note 'FOOBAR'
                Bar
                }
                """,
                "Foo",
                new[] { "Bar" },
                "FOOBAR")]
    [InlineData("""
                enum Foo {
                Bar
                Fub
                Note 'FOOBAR'
                }
                """,
                "Foo",
                new[] { "Bar", "Fub" },
                "FOOBAR")]
    [InlineData("enum Name{Foo,Bar}", "Name", new[] { "Foo", "Bar" })]
    [InlineData("enum Name{Foo=Bar}", "Name", new[] { "Bar" })]
    [InlineData("enum Name{Foo=Bar{Note FOOBAR}}", "Name", new[] { "Bar" })]
    [InlineData("enum Name{ Foo = Bar { Note FOOBAR }, FUB }", "Name", new[] { "Bar", "FUB" })]
    public void ParseEnum_WithValidSyntax_ReturnsCorrectExpression(
        string   syntax,
        string   name,
        string[] values,
        string?  note = null) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Enum.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(name, term.Name);
        Assert.NotNull(term.Values);
        Assert.Equal(values, term.Values.Select(v => v.Value.Body).ToArray());
        Assert.Equal(note, term.Note?.Comment);
    }
}

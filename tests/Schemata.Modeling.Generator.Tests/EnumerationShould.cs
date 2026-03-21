using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class EnumerationShould
{
    [Fact]
    public void ParseBasicEnum() {
        var input  = "Enum Status {\n  Draft\n  Published\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Status", result.Name);
        Assert.Equal(2, result.Values.Length);
        Assert.Equal("Draft", result.Values[0].Name);
        Assert.Equal("Published", result.Values[1].Name);
    }

    [Fact]
    public void ParseEnumWithAssignment() {
        var input  = "Enum Status {\n  Draft = 0\n  Published = 1\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        var v0 = result.Values[0];
        Assert.IsType<NumberLiteral>(v0.Assignment);
        Assert.Equal("0", ((NumberLiteral)v0.Assignment).Raw);
    }

    [Fact]
    public void ParseEnumWithCommas() {
        var input  = "Enum Status { Draft, Published }";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result.Values.Length);
    }

    [Fact]
    public void ParseEnumValueWithNotes() {
        var input  = "Enum Status {\n  Draft {\n    Note 'A draft'\n  }\n  Published\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Values[0].Notes);
        Assert.Equal("A draft", result.Values[0].Notes[0].Text);
    }

    [Fact]
    public void ParseEnumWithTopLevelNote() {
        var input  = "Enum Status {\n  Note 'Status values'\n  Draft\n  Published\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Notes);
        Assert.Equal(2, result.Values.Length);
    }

    [Fact]
    public void ParseCaseInsensitive() {
        var input  = "enum Status { Draft }";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Status", result.Name);
    }
}

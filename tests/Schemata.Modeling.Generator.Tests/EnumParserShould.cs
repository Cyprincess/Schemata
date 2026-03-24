using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class EnumParserShould
{
    [Fact]
    public void Parse_BasicEnum() {
        var input  = "Enum Status {\n  Draft\n  Published\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Status", result.Name);
        Assert.Equal(2, result.Values.Length);
        Assert.Equal("Draft", result.Values[0].Name);
        Assert.Equal("Published", result.Values[1].Name);
    }

    [Fact]
    public void Parse_EnumWithExplicitAssignments() {
        var input  = "Enum Status {\n  Draft = 0\n  Published = 1\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        var v0  = result.Values[0];
        var num = Assert.IsType<NumberLiteral>(v0.Assignment);
        Assert.Equal("0", num.Raw);
        var v1   = result.Values[1];
        var num1 = Assert.IsType<NumberLiteral>(v1.Assignment);
        Assert.Equal("1", num1.Raw);
    }

    [Fact]
    public void Parse_EnumWithCommas() {
        var input  = "Enum Status { Draft, Published }";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result.Values.Length);
    }

    [Fact]
    public void Parse_EnumValueWithNotes() {
        var input  = "Enum Status {\n  Draft {\n    Note 'A draft'\n  }\n  Published\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Values[0].Notes);
        Assert.Equal("A draft", result.Values[0].Notes[0].Text);
        Assert.Equal(0, result.Values[1].Notes.Length);
    }

    [Fact]
    public void Parse_EnumWithTopLevelNote() {
        var input  = "Enum Status {\n  Note 'Status values'\n  Draft\n  Published\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Notes);
        Assert.Equal("Status values", result.Notes[0].Text);
        Assert.Equal(2, result.Values.Length);
    }

    [Fact]
    public void Parse_CaseInsensitive() {
        var input  = "enum Status { Draft }";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Status", result.Name);
    }

    [Fact]
    public void Parse_EnumValueWithStringAssignment() {
        var input  = "Enum Color {\n  Red = 'red'\n  Blue = 'blue'\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        var v0  = result.Values[0];
        var lit = Assert.IsType<Literal>(v0.Assignment);
        Assert.Equal("red", lit.Value);
    }

    [Fact]
    public void Parse_SingleValueEnum() {
        var input  = "Enum Singleton {\n  Only\n}";
        var result = Parser.Enumeration.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Values);
        Assert.Equal("Only", result.Values[0].Name);
    }
}

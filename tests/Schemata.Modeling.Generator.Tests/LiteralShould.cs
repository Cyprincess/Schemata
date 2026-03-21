using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class LiteralShould
{
    [Theory]
    [InlineData("'hello'", "hello")]
    [InlineData("\"world\"", "world")]
    public void ParseQuotedString(string input, string expected) {
        var result = Parser.Literal.Parse(input);
        var s      = Assert.IsType<Literal>(result);
        Assert.Equal(expected, s.Value);
    }

    [Theory]
    [InlineData("'''multi\nline'''", "multi\nline")]
    public void ParseTripleSingleQuotedString(string input, string expected) {
        var result = Parser.Literal.Parse(input);
        var s      = Assert.IsType<Literal>(result);
        Assert.Equal(expected, s.Value);
    }

    [Fact]
    public void ParseTripleDoubleQuotedString() {
        var input  = "\"\"\"triple\ndouble\"\"\"";
        var result = Parser.Literal.Parse(input);
        var s      = Assert.IsType<Literal>(result);
        Assert.Equal("triple\ndouble", s.Value);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("-7", "-7")]
    [InlineData("3.14", "3.14")]
    public void ParseNumber(string input, string expected) {
        var result = Parser.Literal.Parse(input);
        var n      = Assert.IsType<NumberLiteral>(result);
        Assert.Equal(expected, n.Raw);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    public void ParseBoolean(string input, bool expected) {
        var result = Parser.Literal.Parse(input);
        var b      = Assert.IsType<BooleanLiteral>(result);
        Assert.Equal(expected, b.Value);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("NULL")]
    public void ParseNull(string input) {
        var result = Parser.Literal.Parse(input);
        Assert.IsType<NullLiteral>(result);
    }
}

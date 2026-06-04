using System.Linq;
using Parlot;
using Schemata.Expressions.Cel.Expressions;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelParserShould
{
    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-123", -123L)]
    [InlineData("0x10", 16L)]
    [InlineData("-0x10", -16L)]
    [InlineData("123u", 123UL)]
    [InlineData("0x10u", 16UL)]
    [InlineData("1.5", 1.5d)]
    [InlineData("1e2", 100d)]
    public void Parse_NumberLiteralThroughGrammar_ReturnsNumericConstant(string source, object expected) {
        var node = Assert.IsType<CelConstant>(CelParser.Expression.Parse(source));

        Assert.Equal(expected, node.Value);
    }

    [Theory]
    [InlineData("r\"\"", "")]
    [InlineData("r\"\"\"\"\"\"", "")]
    [InlineData("r''''''", "")]
    public void Parse_RawStringLiteralThroughGrammar_ReturnsStringConstant(string source, string expected) {
        var node = Assert.IsType<CelConstant>(CelParser.Expression.Parse(source));

        Assert.Equal(expected, node.Value);
    }

    [Theory]
    [InlineData("b\"\"", new byte[0])]
    [InlineData("b'ÿ'", new byte[] { 0xc3, 0xbf })]
    [InlineData("b'\\000\\xff'", new byte[] { 0x00, 0xff })]
    public void Parse_BytesLiteralThroughGrammar_ReturnsByteArrayConstant(string source, byte[] expected) {
        var node  = Assert.IsType<CelConstant>(CelParser.Expression.Parse(source));
        var bytes = Assert.IsType<byte[]>(node.Value);

        Assert.True(expected.SequenceEqual(bytes));
    }

    [Fact]
    public void Parse_TernaryOperator_ReturnsConditionalNode() {
        var node = Assert.IsType<CelConditional>(CelParser.Expression.Parse("ready ? 'go' : 'stop'"));

        Assert.IsType<CelIdentifier>(node.Condition);
        Assert.Equal("go", Assert.IsType<CelConstant>(node.WhenTrue).Value);
        Assert.Equal("stop", Assert.IsType<CelConstant>(node.WhenFalse).Value);
    }

    [Fact]
    public void Parse_MemberFunctionCall_ReturnsMemberCallNode() {
        var node = Assert.IsType<CelMemberCall>(CelParser.Expression.Parse("full_name.contains('Ali')"));

        Assert.IsType<CelIdentifier>(node.Target);
        Assert.Equal("contains", node.Name);
        Assert.Single(node.Args);
    }

    [Fact]
    public void Parse_InOperator_ReturnsBinaryNode() {
        var node = Assert.IsType<CelBinary>(CelParser.Expression.Parse("2 in [1, 2, 3]"));

        Assert.Equal("in", node.Operator);
        Assert.IsType<CelList>(node.Right);
    }

    [Fact]
    public void Parse_ListMacro_ReturnsMemberCallNode() {
        var node = Assert.IsType<CelMemberCall>(CelParser.Expression.Parse("scores.exists(score, score > 80)"));

        Assert.Equal("exists", node.Name);
        Assert.Equal(2, node.Args.Count);
    }

    [Theory]
    [InlineData("'\\q'")]
    [InlineData("b'\\q'")]
    public void Parse_InvalidEscapeSequence_ThrowsParseException(string source) {
        Assert.Throws<ParseException>(() => CelParser.Expression.Parse(source));
    }
}

using System.Linq;
using Parlot;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;
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
    [InlineData("18446744073709551615u", 18446744073709551615UL)]
    [InlineData("-9223372036854775808", -9223372036854775808L)]
    [InlineData("0x7FFFFFFFFFFFFFFF", 9223372036854775807L)]
    [InlineData("-0x7FFFFFFFFFFFFFFF", -9223372036854775807L)]
    [InlineData("-0x8000000000000000", -9223372036854775808L)]
    [InlineData("-0x0", 0L)]
    [InlineData("0xFFFFFFFFFFFFFFFFu", 18446744073709551615UL)]
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
    [InlineData("b\"😀\"", new byte[] { 0xF0, 0x9F, 0x98, 0x80 })]
    [InlineData("b\"aé😀\\x42\"", new byte[] { 0x61, 0xC3, 0xA9, 0xF0, 0x9F, 0x98, 0x80, 0x42 })]
    [InlineData("b\"\\x41z\"", new byte[] { 0x41, 0x7A })]
    public void Parse_BytesLiteralThroughGrammar_ReturnsByteArrayConstant(string source, byte[] expected) {
        var node  = Assert.IsType<CelConstant>(CelParser.Expression.Parse(source));
        var bytes = Assert.IsType<byte[]>(node.Value);

        Assert.True(expected.SequenceEqual(bytes));
    }

    [Theory]
    [InlineData("' '", " ")]
    [InlineData("'  '", "  ")]
    [InlineData("' hi '", " hi ")]
    [InlineData("'a b c'", "a b c")]
    [InlineData("\" leading\"", " leading")]
    public void Parse_StringLiteralWithWhitespace_PreservesContent(string source, string expected) {
        var node = Assert.IsType<CelConstant>(CelParser.Expression.Parse(source));

        Assert.Equal(expected, node.Value);
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

    [Theory]
    [InlineData("18446744073709551616u")]
    [InlineData("0xFFFFFFFFFFFFFFFF")]
    [InlineData("-0xFFFFFFFFFFFFFFFF")]
    [InlineData("0x8000000000000000")]
    [InlineData("-0x8000000000000001")]
    [InlineData("0x10000000000000000u")]
    [InlineData("'\\u12'")]
    [InlineData("'\\U00110000'")]
    [InlineData("'\\777'")]
    [InlineData("b'\\x1'")]
    [InlineData("b'\\777'")]
    public void Parse_MalformedLiteral_ThrowsParseException(string source) {
        Assert.Throws<ParseException>(() => CelParser.Expression.Parse(source));
    }

    [Fact]
    public void Parse_MalformedLiteral_ThroughCompilerSurfacesExpressionException() {
        Assert.Throws<ExpressionException>(() => new CelCompiler().Parse("18446744073709551616u"));
    }
}

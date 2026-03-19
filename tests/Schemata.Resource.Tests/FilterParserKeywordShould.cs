using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Tests;

public class FilterParserKeywordShould
{
    [Theory]
    [InlineData("x.and = 1")]
    [InlineData("x.or = 1")]
    [InlineData("x.not = 1")]
    [InlineData("msg.and.or.not = 'test'")]
    public void ParseKeywordAsFieldName(string input) {
        var result = Parser.Filter.Parse(input);
        Assert.NotNull(result);
    }

    [Fact]
    public void KeywordFieldInMemberChain() {
        var result = Parser.Filter.Parse("obj.and = 'value'");
        Assert.NotNull(result);
        var str = result.ToString();
        Assert.Contains("and", str!.ToLower());
    }

    [Fact]
    public void StillParseStandaloneKeywords() {
        // "AND", "OR", "NOT" should still work as logical operators
        var result = Parser.Filter.Parse("a = 1 AND b = 2");
        Assert.NotNull(result);
    }

    [Fact]
    public void StillParseNotOperator() {
        var result = Parser.Filter.Parse("NOT a = 1");
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("x.ANDROID = 1")]
    [InlineData("x.ORDER = 1")]
    [InlineData("x.NOTHING = 1")]
    public void ParseKeywordPrefixAsFieldName(string input) {
        var result = Parser.Filter.Parse(input);
        Assert.NotNull(result);
    }

    [Fact]
    public void KeywordPrefixInMemberChain_FullValue() {
        var result = Parser.Filter.Parse("x.ANDROID = 1");
        Assert.NotNull(result);
        Assert.Equal("[= \"x\".\"ANDROID\" 1]", result.ToString());
    }
}

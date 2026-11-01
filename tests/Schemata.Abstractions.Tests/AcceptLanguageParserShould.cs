using Schemata.Abstractions.Globalization;
using Xunit;

namespace Schemata.Abstractions.Tests;

public class AcceptLanguageParserShould
{
    [Fact]
    public void Parse_ForNullValues_ReturnsNull() {
        Assert.Null(AcceptLanguageParser.Parse(null));
    }

    [Fact]
    public void Parse_ForHighestQuality_ReturnsThatCulture() {
        var culture = AcceptLanguageParser.Parse(["en;q=0.1", "zh-CN;q=0.9"]);
        Assert.NotNull(culture);
        Assert.Equal("zh-CN", culture.Name);
    }

    [Fact]
    public void Parse_ForSegmentWithoutQuality_DefaultsToHighest() {
        var culture = AcceptLanguageParser.Parse(["fr;q=0.5", "de"]);
        Assert.NotNull(culture);
        Assert.Equal("de", culture.Name);
    }

    [Fact]
    public void Parse_ForWildcardAndZeroQuality_SkipsThem() {
        Assert.Null(AcceptLanguageParser.Parse(["*", "en;q=0"]));
    }

    [Fact]
    public void Parse_ForInvalidTags_FallsBackToNextCandidate() {
        var culture = AcceptLanguageParser.Parse(["invalid culture;q=1", "ja;q=0.8"]);
        Assert.NotNull(culture);
        Assert.Equal("ja", culture.Name);
    }

    [Fact]
    public void Parse_ForEqualQuality_KeepsArrivalOrder() {
        var culture = AcceptLanguageParser.Parse(["en;q=0.9", "fr;q=0.9"]);
        Assert.NotNull(culture);
        Assert.Equal("en", culture.Name);
    }

    [Theory]
    [InlineData("en", "fr;q=NaN", "zh-CN;q=0.5", "en")]
    [InlineData("de;q=Infinity", "ja;q=0.8", "", "ja")]
    [InlineData("de;q=1.5", "ko;q=0.7", "", "ko")]
    public void Parse_ForInvalidQuality_Disqualifies_The_Member(string first, string second, string third, string expected) {
        // NaN compares false against every bound, so an invalid qvalue cannot win by comparison alone.
        var culture = AcceptLanguageParser.Parse([first, second, third]);

        Assert.NotNull(culture);
        Assert.Equal(expected, culture.Name);
    }
}

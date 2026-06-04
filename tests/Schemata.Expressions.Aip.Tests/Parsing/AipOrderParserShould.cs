using System.Linq;
using Schemata.Expressions.Aip.Values;
using Xunit;

namespace Schemata.Expressions.Aip.Tests.Parsing;

public class AipOrderParserShould
{
    [Theory]
    [InlineData("a", new[] { "a" }, new[] { Ordering.Ascending })]
    [InlineData("a DESC", new[] { "a" }, new[] { Ordering.Descending })]
    [InlineData("a ASC", new[] { "a" }, new[] { Ordering.Ascending })]
    [InlineData("a desc", new[] { "a" }, new[] { Ordering.Descending })]
    [InlineData("a asc", new[] { "a" }, new[] { Ordering.Ascending })]
    [InlineData("a,b", new[] { "a", "b" }, new[] { Ordering.Ascending, Ordering.Ascending })]
    [InlineData("a DESC,b", new[] { "a", "b" }, new[] { Ordering.Descending, Ordering.Ascending })]
    [InlineData("a,b DESC", new[] { "a", "b" }, new[] { Ordering.Ascending, Ordering.Descending })]
    [InlineData("a DESC,b ASC", new[] { "a", "b" }, new[] { Ordering.Descending, Ordering.Ascending })]
    public void ParseOrder_ReturnsExpected(string input, string[] expectedFields, Ordering[] expectedDirections) {
        var result = AipParser.Order.Parse(input)?.ToList();

        Assert.NotNull(result);
        Assert.Equal(expectedFields.Length, result.Count);
        for (var i = 0; i < expectedFields.Length; i++) {
            Assert.Equal(expectedFields[i], ((Text)result[i].Key.Value).Value);
            Assert.Equal(expectedDirections[i], result[i].Value);
        }
    }

    [Fact]
    public void ParseOrder_NestedField_PreservesPathSegments() {
        var result = AipParser.Order.Parse("foo.bar")?.ToList();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Ordering.Ascending, result[0].Value);

        var member = result[0].Key;
        Assert.Equal("foo", ((Text)member.Value).Value);
        Assert.Single(member.Fields);
        Assert.Equal("bar", ((Text)member.Fields[0]).Value);
    }
}

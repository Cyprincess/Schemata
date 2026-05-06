using System.Linq;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Tests.Filtering;

public class OrderParserShould
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
        var result = Parser.Order.Parse(input)?.ToList();

        Assert.NotNull(result);
        Assert.Equal(expectedFields.Length, result.Count);
        for (var i = 0; i < expectedFields.Length; i++) {
            Assert.Equal(expectedFields[i], result[i].Key.Value.Value!.ToString());
            Assert.Equal(expectedDirections[i], result[i].Value);
        }
    }

    [Fact]
    public void ParseOrder_NestedField_Ascending() {
        var result = Parser.Order.Parse("foo.bar")?.ToList();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Ordering.Ascending, result[0].Value);
    }
}

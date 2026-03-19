using System.Linq;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Tests;

public class OrderParserShould
{
    [Fact]
    public void ParseSingleField() {
        var result = Parser.Order.Parse("name");
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Ordering.Ascending, result.Values.First());
    }

    [Fact]
    public void ParseDescending() {
        var result = Parser.Order.Parse("name DESC");
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Ordering.Descending, result.Values.First());
    }

    [Fact]
    public void ParseAscending() {
        var result = Parser.Order.Parse("name ASC");
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Ordering.Ascending, result.Values.First());
    }

    [Fact]
    public void ParseMultipleFields() {
        var result = Parser.Order.Parse("name, created_at DESC");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseMultipleFields_CorrectOrdering() {
        var result = Parser.Order.Parse("name, created_at DESC")
                          ?.ToDictionary(kv => kv.Key.Value.Value!.ToString()!, kv => kv.Value);
        Assert.NotNull(result);
        Assert.Equal(Ordering.Ascending, result["name"]);
        Assert.Equal(Ordering.Descending, result["created_at"]);
    }

    [Fact]
    public void ParseNestedField() {
        var result = Parser.Order.Parse("foo.bar DESC");
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Ordering.Descending, result.Values.First());
    }

    [Fact]
    public void ParseNestedField_MemberChain() {
        var result = Parser.Order.Parse("foo.bar DESC");
        Assert.NotNull(result);
        var member = result.Keys.First();
        Assert.Equal("\"foo\".\"bar\"", member.ToString());
    }

    [Fact]
    public void ParseWithRedundantWhitespace() {
        var result = Parser.Order.Parse("  name  ,  created_at  DESC  ");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DefaultIsAscending() {
        var result = Parser.Order.Parse("a, b");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.All(result.Values, v => Assert.Equal(Ordering.Ascending, v));
    }

    [Fact]
    public void ParseMixedOrdering() {
        var result = Parser.Order.Parse("a ASC, b DESC, c")
                          ?.ToDictionary(kv => kv.Key.Value.Value!.ToString()!, kv => kv.Value);
        Assert.NotNull(result);
        Assert.Equal(Ordering.Ascending, result["a"]);
        Assert.Equal(Ordering.Descending, result["b"]);
        Assert.Equal(Ordering.Ascending, result["c"]);
    }

    [Fact]
    public void ParseCaseInsensitive_Asc() {
        var result = Parser.Order.Parse("name asc");
        Assert.NotNull(result);
        Assert.Equal(Ordering.Ascending, result.Values.First());
    }

    [Fact]
    public void ParseCaseInsensitive_Desc() {
        var result = Parser.Order.Parse("name desc");
        Assert.NotNull(result);
        Assert.Equal(Ordering.Descending, result.Values.First());
    }
}

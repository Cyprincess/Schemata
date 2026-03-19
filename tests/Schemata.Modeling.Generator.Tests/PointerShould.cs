using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class PointerShould
{
    [Fact]
    public void ParseSingleColumn() {
        var result = Parser.Pointer.Parse("Index category_id");
        Assert.Single(result.Columns);
        Assert.Equal("category_id", result.Columns[0]);
    }

    [Fact]
    public void ParseMultipleColumns() {
        var result = Parser.Pointer.Parse("Index user_id creation_date");
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("user_id", result.Columns[0]);
        Assert.Equal("creation_date", result.Columns[1]);
    }

    [Fact]
    public void ParseWithOptions() {
        var result = Parser.Pointer.Parse("Index category_id [b tree]");
        Assert.Single(result.Options);
        Assert.Equal(PointerOption.BTree, result.Options[0]);
    }

    [Fact]
    public void ParseWithNote() {
        var input  = "Index category_id {\n  Note 'index on category'\n}";
        var result = Parser.Pointer.Parse(input);
        Assert.Single(result.Notes);
    }

    [Fact]
    public void ParseCaseInsensitive() {
        var result = Parser.Pointer.Parse("index category_id");
        Assert.Single(result.Columns);
    }
}

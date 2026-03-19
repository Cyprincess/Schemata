using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class OptionsShould
{
    [Fact]
    public void ParseRequired() {
        var result = Parser.FieldOptions.Parse("[required]");
        Assert.Single(result);
        Assert.Equal(FieldOption.Required, result[0]);
    }

    [Fact]
    public void ParseUnique() {
        var result = Parser.FieldOptions.Parse("[unique]");
        Assert.Single(result);
        Assert.Equal(FieldOption.Unique, result[0]);
    }

    [Fact]
    public void ParsePrimaryKey() {
        var result = Parser.FieldOptions.Parse("[primary key]");
        Assert.Single(result);
        Assert.Equal(FieldOption.PrimaryKey, result[0]);
    }

    [Fact]
    public void ParsePrimaryKeyCamelCase() {
        var result = Parser.FieldOptions.Parse("[PrimaryKey]");
        Assert.Single(result);
        Assert.Equal(FieldOption.PrimaryKey, result[0]);
    }

    [Fact]
    public void ParseNotNull() {
        var result = Parser.FieldOptions.Parse("[not null]");
        Assert.Single(result);
        Assert.Equal(FieldOption.Required, result[0]);
    }

    [Fact]
    public void ParseBTree() {
        var result = Parser.FieldOptions.Parse("[b tree]");
        Assert.Single(result);
        Assert.Equal(FieldOption.BTree, result[0]);
    }

    [Fact]
    public void ParseHash() {
        var result = Parser.FieldOptions.Parse("[hash]");
        Assert.Single(result);
        Assert.Equal(FieldOption.Hash, result[0]);
    }

    [Fact]
    public void ParseAutoIncrement() {
        var result = Parser.FieldOptions.Parse("[auto increment]");
        Assert.Single(result);
        Assert.Equal(FieldOption.AutoIncrement, result[0]);
    }

    [Fact]
    public void ParseAutoIncrementCamelCase() {
        var result = Parser.FieldOptions.Parse("[AutoIncrement]");
        Assert.Single(result);
        Assert.Equal(FieldOption.AutoIncrement, result[0]);
    }

    [Fact]
    public void ParseMultipleFieldOptions() {
        var result = Parser.FieldOptions.Parse("[primary key, auto increment]");
        Assert.Equal(2, result.Length);
        Assert.Equal(FieldOption.PrimaryKey, result[0]);
        Assert.Equal(FieldOption.AutoIncrement, result[1]);
    }

    [Fact]
    public void ParseViewOptionOmit() {
        var result = Parser.ViewOptions.Parse("[omit]");
        Assert.Single(result);
        Assert.Equal(ViewOption.Omit, result[0]);
    }

    [Fact]
    public void ParseViewOptionOmitAll() {
        var result = Parser.ViewOptions.Parse("[omit all]");
        Assert.Single(result);
        Assert.Equal(ViewOption.OmitAll, result[0]);
    }

    [Fact]
    public void ParseViewOptionOmitAllCamelCase() {
        var result = Parser.ViewOptions.Parse("[OmitAll]");
        Assert.Single(result);
        Assert.Equal(ViewOption.OmitAll, result[0]);
    }

    [Fact]
    public void ParsePointerOptionUnique() {
        var result = Parser.PointerOptions.Parse("[unique]");
        Assert.Single(result);
        Assert.Equal(PointerOption.Unique, result[0]);
    }

    [Fact]
    public void ParsePointerOptionBTree() {
        var result = Parser.PointerOptions.Parse("[b tree]");
        Assert.Single(result);
        Assert.Equal(PointerOption.BTree, result[0]);
    }

    [Fact]
    public void ParsePointerOptionHash() {
        var result = Parser.PointerOptions.Parse("[hash]");
        Assert.Single(result);
        Assert.Equal(PointerOption.Hash, result[0]);
    }
}

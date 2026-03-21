using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class TraitShould
{
    [Fact]
    public void ParseBasicTrait() {
        var input  = "Trait Identifier {\n  Note 'Primary Key'\n  long id [primary key]\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Identifier", result.Name);
        Assert.Single(result.Fields);
        Assert.Single(result.Notes);
    }

    [Fact]
    public void ParseTraitWithBases() {
        var input  = "Trait Entity : Identifier, Timestamp {\n  long id\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result.Bases.Length);
        Assert.Equal("Identifier", result.Bases[0]);
    }

    [Fact]
    public void ParseTraitWithUse() {
        var input  = "Trait Entity {\n  Use Identifier, Timestamp\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Uses);
    }

    [Fact]
    public void ParseCaseInsensitive() {
        var input  = "trait Foo {\n  string bar\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Foo", result.Name);
    }
}

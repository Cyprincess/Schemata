using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class UseShould
{
    [Fact]
    public void ParseSingleName() {
        var result = Parser.Use.Parse("Use Identifier");
        Assert.Single(result.QualifiedNames);
        Assert.Equal("Identifier", result.QualifiedNames[0]);
    }

    [Fact]
    public void ParseMultipleNames() {
        var result = Parser.Use.Parse("Use Identifier, Timestamp");
        Assert.Equal(2, result.QualifiedNames.Length);
        Assert.Equal("Identifier", result.QualifiedNames[0]);
        Assert.Equal("Timestamp", result.QualifiedNames[1]);
    }

    [Fact]
    public void ParseQualifiedName() {
        var result = Parser.Use.Parse("Use Foo.Bar.Baz");
        Assert.Equal("Foo.Bar.Baz", result.QualifiedNames[0]);
    }

    [Fact]
    public void ParseCaseInsensitive() {
        var result = Parser.Use.Parse("use Identifier");
        Assert.Single(result.QualifiedNames);
    }
}

using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class PropertyShould
{
    [Fact]
    public void ParseDefaultString() {
        var result = Parser.Property.Parse("Default 'Published'");
        Assert.Equal("Default", result.Key);
        var v = Assert.IsType<StringLiteral>(result.Value);
        Assert.Equal("Published", v.Value);
    }

    [Fact]
    public void ParseLengthNumber() {
        var result = Parser.Property.Parse("Length 8");
        Assert.Equal("Length", result.Key);
        Assert.IsType<NumberLiteral>(result.Value);
    }

    [Fact]
    public void ParseAlgorithmReference() {
        var result = Parser.Property.Parse("Algorithm Hash.SHA256");
        Assert.Equal("Algorithm", result.Key);
        var r = Assert.IsType<Reference>(result.Value);
        Assert.Equal("Hash.SHA256", r.QualifiedName);
    }

    [Fact]
    public void ParseDefaultNull() {
        var result = Parser.Property.Parse("Default null");
        Assert.Equal("Default", result.Key);
        Assert.IsType<NullLiteral>(result.Value);
    }

    [Fact]
    public void ParseDefaultBoolean() {
        var result = Parser.Property.Parse("Enabled true");
        Assert.Equal("Enabled", result.Key);
        Assert.IsType<BooleanLiteral>(result.Value);
    }
}

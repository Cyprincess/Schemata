using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class NamespaceShould
{
    [Theory]
    [InlineData("Namespace Foo", "Foo")]
    [InlineData("Namespace Foo.Bar.Baz", "Foo.Bar.Baz")]
    [InlineData("namespace foo", "foo")]
    public void ParseNamespace(string input, string expected) {
        var result = Parser.Namespace.Parse(input);
        Assert.Equal(expected, result);
    }
}

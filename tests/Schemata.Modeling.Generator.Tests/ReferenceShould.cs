using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ReferenceShould
{
    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("category.id", "category.id")]
    [InlineData("Hash.SHA256", "Hash.SHA256")]
    public void ParseQualifiedName(string input, string expected) {
        var result = Parser.Expression.Parse(input);
        var r      = Assert.IsType<Reference>(result);
        Assert.Equal(expected, r.QualifiedName);
    }
}

using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ExpressionShould
{
    [Fact]
    public void DisambiguateLiteralOverReference() {
        // "true" should be BooleanLiteral, not Reference("true")
        var result = Parser.Expression.Parse("true");
        Assert.IsType<BooleanLiteral>(result);
    }

    [Fact]
    public void DisambiguateNullOverReference() {
        var result = Parser.Expression.Parse("null");
        Assert.IsType<NullLiteral>(result);
    }

    [Fact]
    public void DisambiguateFunctionOverReference() {
        // "foo()" should be FunctionCall, not Reference
        var result = Parser.Expression.Parse("foo()");
        Assert.IsType<FunctionCall>(result);
    }

    [Fact]
    public void DisambiguateReferenceWithoutParens() {
        // "foo" without () should be Reference
        var result = Parser.Expression.Parse("foo");
        Assert.IsType<Reference>(result);
    }

    [Fact]
    public void ParseLiteral() {
        var result = Parser.Expression.Parse("'hello'");
        Assert.IsType<Literal>(result);
    }

    [Fact]
    public void ParseNumberLiteral() {
        var result = Parser.Expression.Parse("42");
        Assert.IsType<NumberLiteral>(result);
    }
}

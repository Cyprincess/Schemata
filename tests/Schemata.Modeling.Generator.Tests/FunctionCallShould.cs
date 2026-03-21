using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class FunctionCallShould
{
    [Fact]
    public void ParseNoArgs() {
        var result = Parser.Expression.Parse("now()");
        var fn     = Assert.IsType<FunctionCall>(result);
        Assert.Equal("now", fn.Name);
        Assert.Empty(fn.Arguments);
    }

    [Fact]
    public void ParseWithRefArg() {
        var result = Parser.Expression.Parse("obfuscate(email_address)");
        var fn     = Assert.IsType<FunctionCall>(result);
        Assert.Equal("obfuscate", fn.Name);
        Assert.Single(fn.Arguments);
        var arg = Assert.IsType<Reference>(fn.Arguments[0]);
        Assert.Equal("email_address", arg.QualifiedName);
    }

    [Fact]
    public void ParseMultipleArgs() {
        var result = Parser.Expression.Parse("substring(input, 0, 5)");
        var fn     = Assert.IsType<FunctionCall>(result);
        Assert.Equal("substring", fn.Name);
        Assert.Equal(3, fn.Arguments.Length);
        Assert.IsType<Reference>(fn.Arguments[0]);
        Assert.IsType<NumberLiteral>(fn.Arguments[1]);
        Assert.IsType<NumberLiteral>(fn.Arguments[2]);
    }

    [Fact]
    public void ParseWithSpaces() {
        var result = Parser.Expression.Parse("substring( input , 0 , 5 )");
        var fn     = Assert.IsType<FunctionCall>(result);
        Assert.Equal(3, fn.Arguments.Length);
    }

    [Fact]
    public void ParseLiteralArg() {
        var result = Parser.Expression.Parse("greet('hello')");
        var fn     = Assert.IsType<FunctionCall>(result);
        var arg    = Assert.IsType<Literal>(fn.Arguments[0]);
        Assert.Equal("hello", arg.Value);
    }
}

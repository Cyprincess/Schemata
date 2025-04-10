using System;
using System.Collections.Generic;
using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Tests;

public class FilterContainerShould
{
    [Fact]
    public void Build_WithSimpleAndExpression_ReturnsCompiledExpression() {
        var filter = Parser.Filter.Parse("a b AND c AND d");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("q", typeof(string)).Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("a b c d"));
        Assert.False(func("a b c"));
    }

    [Fact]
    public void Build_WithSimpleOrExpression_ReturnsCompiledExpression() {
        var filter = Parser.Filter.Parse("New York Giants OR Yankees");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("q", typeof(string)).Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("New York Giants"));
        Assert.False(func("New Giants Yankees"));
    }

    [Fact]
    public void Build_WithNumericComparison_ReturnsCompiledExpression() {
        var filter = Parser.Filter.Parse("a < 10 OR a >= 100");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("a", typeof(long)).Build();
        Assert.NotNull(expression);

        var expected = new Func<long, bool>(a => a is < 10 or >= 100);
        var actual   = (Func<long, bool>)expression.Compile();

        Assert.Equal(expected(10), actual(10));
        Assert.Equal(expected(100), actual(100));
    }

    [Fact]
    public void Build_WithNestedProperty_ReturnsCompiledExpression() {
        var filter = Parser.Filter.Parse("expr.type_map.1.type = bar");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("expr", typeof(MyVector4)).Build();
        Assert.NotNull(expression);

        var func = (Func<MyVector4, bool>)expression.Compile();

        var vector = new MyVector4 {
            type_map = [
                new() { type = "foo" },
                new() { type = "bar" },
            ],
        };

        Assert.True(func(vector));
    }

    [Fact]
    public void Build_WithFunctionCall_ReturnsCompiledExpression() {
        var filter = Parser.Filter.Parse("(msg.endsWith('world') AND retries < 10)");
        Assert.NotNull(filter);

        var expression = Container.Build(filter)
                                  .AllowFunction<string>("endsWith")
                                  .Bind("msg", typeof(string))
                                  .Bind("retries", typeof(int))
                                  .Build();
        Assert.NotNull(expression);

        var func = (Func<string, int, bool>)expression.Compile();
        Assert.True(func("hello world", 9));
        Assert.False(func("hello world", 10));
    }

    #region Nested type: MyVector4

    public class MyVector4
    {
        public IEnumerable<MyType> type_map { get; set; } = [];

        #region Nested type: MyType

        public class MyType
        {
            public string type { get; set; } = string.Empty;
        }

        #endregion
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
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

        var vector = new MyVector4 { type_map = [new() { type = "foo" }, new() { type = "bar" }] };

        Assert.True(func(vector));
    }

    [Fact]
    public void Build_WithFunctionCall_ReturnsCompiledExpression() {
        var filter = Parser.Filter.Parse("(msg.endsWith('world') AND retries < 10)");
        Assert.NotNull(filter);

        var expression = Container.Build(filter)
                                  .RegisterFunction("endsWith", (args, ctx) => {
                                       var method = ctx.GetMethod(typeof(string), "EndsWith", [typeof(string)]);
                                       return Expression.Call(args[0], method!, args[1]);
                                   })
                                  .Bind("msg", typeof(string))
                                  .Bind("retries", typeof(int))
                                  .Build();
        Assert.NotNull(expression);

        var func = (Func<string, int, bool>)expression.Compile();
        Assert.True(func("hello world", 9));
        Assert.False(func("hello world", 10));
    }

    [Fact]
    public void Build_WithRegisteredFunction_Standalone() {
        var filter = Parser.Filter.Parse("startsWith(name, 'hel')");
        Assert.NotNull(filter);

        var expression = Container.Build(filter)
                                  .RegisterFunction("startsWith", (args, ctx) => {
                                       var method = ctx.GetMethod(typeof(string), "StartsWith", [typeof(string)]);
                                       return Expression.Call(args[0], method!, args[1]);
                                   })
                                  .Bind("name", typeof(string))
                                  .Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("hello world"));
        Assert.False(func("goodbye hello"));
    }

    [Fact]
    public void Build_WithWildcardContains_CompilesContains() {
        var filter = Parser.Filter.Parse("name = '*ello*'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("name", typeof(string)).Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("hello world"));
        Assert.False(func("goodbye"));
    }

    [Fact]
    public void Build_WithWildcardPrefix_CompilesStartsWith() {
        var filter = Parser.Filter.Parse("name = 'hel*'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("name", typeof(string)).Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("hello world"));
        Assert.False(func("goodbye hello"));
    }

    [Fact]
    public void Build_WithWildcardSuffix_CompilesEndsWith() {
        var filter = Parser.Filter.Parse("name = '*rld'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("name", typeof(string)).Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("hello world"));
        Assert.False(func("hello world!"));
    }

    [Fact]
    public void Build_WithHasOperator_StringContains() {
        var filter = Parser.Filter.Parse("name : 'ello'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("name", typeof(string)).Build();
        Assert.NotNull(expression);

        var func = (Func<string, bool>)expression.Compile();
        Assert.True(func("hello world"));
        Assert.False(func("goodbye"));
    }

    [Fact]
    public void Build_WithHasOperator_ListContains() {
        var filter = Parser.Filter.Parse("tags : 42");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("tags", typeof(List<long>)).Build();
        Assert.NotNull(expression);

        var func = (Func<List<long>, bool>)expression.Compile();
        Assert.True(func([1L, 42L, 100L]));
        Assert.False(func([1L, 2L, 3L]));
    }

    [Fact]
    public void Build_WithHasOperator_DictionaryContainsKey() {
        var filter = Parser.Filter.Parse("metadata : 'key1'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter)
                                  .Bind("metadata", typeof(Dictionary<string, string>))
                                  .Build();
        Assert.NotNull(expression);

        var func = (Func<Dictionary<string, string>, bool>)expression.Compile();
        Assert.True(func(new() { ["key1"]  = "value1" }));
        Assert.False(func(new() { ["key2"] = "value2" }));
    }

    [Fact]
    public void Build_WithHasOperator_PresenceTest() {
        var filter = Parser.Filter.Parse("tags : '*'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("tags", typeof(List<long>)).Build();
        Assert.NotNull(expression);

        var func = (Func<List<long>, bool>)expression.Compile();
        Assert.True(func([1L]));
        Assert.False(func([]));
    }

    [Fact]
    public void Build_WithNotOnBoolExpression_ReturnsLogicalNegation() {
        var filter = Parser.Filter.Parse("NOT a = 1");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("a", typeof(long)).Build();
        Assert.NotNull(expression);

        var func = (Func<long, bool>)expression.Compile();
        Assert.True(func(2));
        Assert.False(func(1));
    }

    [Fact]
    public void Build_WithKeywordAsField_CompilesPropertyAccess() {
        var filter = Parser.Filter.Parse("obj.and = 'test'");
        Assert.NotNull(filter);

        var expression = Container.Build(filter).Bind("obj", typeof(KeywordEntity)).Build();
        Assert.NotNull(expression);

        var func = (Func<KeywordEntity, bool>)expression.Compile();
        Assert.True(func(new() { and  = "test" }));
        Assert.False(func(new() { and = "other" }));
    }

    #region Nested type: KeywordEntity

    public class KeywordEntity
    {
        public string and { get; set; } = string.Empty;
        public string or  { get; set; } = string.Empty;
        public string not { get; set; } = string.Empty;
    }

    #endregion

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

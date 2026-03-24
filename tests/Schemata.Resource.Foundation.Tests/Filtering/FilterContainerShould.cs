using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Filtering;

public class FilterContainerShould
{
    [Fact]
    public void Build_Sequence_MatchesAllTerms() {
        var func = Compile<string>("a b AND c AND d", "q");
        Assert.True(func("a b c d"));
        Assert.False(func("a b c"));
    }

    [Fact]
    public void Build_SequenceWithOr_MatchesAnyBranch() {
        var func = Compile<string>("New York Giants OR Yankees", "q");
        Assert.True(func("New York Giants"));
        Assert.False(func("a b c")); // Neither branch matches
    }

    [Fact]
    public void Build_NumericComparison_MatchesBoundary() {
        var func = Compile<long>("a < 10 OR a >= 100", "a");
        Assert.True(func(5));
        Assert.True(func(100));
        Assert.False(func(10));
        Assert.False(func(99));
    }

    [Fact]
    public void Build_NestedProperty_AccessesIndexedElement() {
        var filter = Parser.Filter.Parse("expr.type_map.1.type = bar");
        Assert.NotNull(filter);

        var expr = Container.Build(filter).Bind("expr", typeof(MyVector4)).Build();
        Assert.NotNull(expr);

        var func   = (Func<MyVector4, bool>)expr.Compile();
        var vector = new MyVector4 { type_map = [new() { type = "foo" }, new() { type = "bar" }] };

        Assert.True(func(vector));
    }

    [Theory]
    [InlineData("name = 'hel*'", "hello world", true)]
    [InlineData("name = 'hel*'", "goodbye hello", false)]
    [InlineData("name = '*rld'", "hello world", true)]
    [InlineData("name = '*rld'", "hello world!", false)]
    [InlineData("name = '*ello*'", "hello world", true)]
    [InlineData("name = '*ello*'", "goodbye", false)]
    public void Build_WildcardEqual_CompilesCorrectStringMatch(string filter, string input, bool expected) {
        var func = Compile<string>(filter, "name");
        Assert.Equal(expected, func(input));
    }

    [Fact]
    public void Build_Has_StringContains() {
        var func = Compile<string>("name : 'ello'", "name");
        Assert.True(func("hello world"));
        Assert.False(func("goodbye"));
    }

    [Fact]
    public void Build_Has_ListContains() {
        var filter = Parser.Filter.Parse("tags : 42");
        Assert.NotNull(filter);
        var expr = Container.Build(filter).Bind("tags", typeof(List<long>)).Build();
        var func = (Func<List<long>, bool>)expr!.Compile();
        Assert.True(func([1L, 42L, 100L]));
        Assert.False(func([1L, 2L, 3L]));
    }

    [Fact]
    public void Build_Has_DictionaryKey() {
        var filter = Parser.Filter.Parse("metadata : 'key1'");
        Assert.NotNull(filter);
        var expr = Container.Build(filter).Bind("metadata", typeof(Dictionary<string, string>)).Build();
        var func = (Func<Dictionary<string, string>, bool>)expr!.Compile();
        Assert.True(func(new() { ["key1"]  = "v1" }));
        Assert.False(func(new() { ["key2"] = "v2" }));
    }

    [Fact]
    public void Build_Has_WildcardPresenceCheck() {
        var filter = Parser.Filter.Parse("tags : '*'");
        Assert.NotNull(filter);
        var expr = Container.Build(filter).Bind("tags", typeof(List<long>)).Build();
        var func = (Func<List<long>, bool>)expr!.Compile();
        Assert.True(func([1L]));
        Assert.False(func([]));
    }

    [Fact]
    public void Build_Has_AnnotationsDictionaryKey() {
        // annotations : "schedule"  — dict key presence (from einride checker tests)
        var filter = Parser.Filter.Parse("annotations : 'schedule'");
        Assert.NotNull(filter);
        var expr = Container.Build(filter).Bind("annotations", typeof(Dictionary<string, string>)).Build();
        var func = (Func<Dictionary<string, string>, bool>)expr!.Compile();
        Assert.True(func(new() { ["schedule"] = "* * * * *" }));
        Assert.False(func(new() { ["other"]   = "value" }));
    }

    [Fact]
    public void Build_Not_LogicalNegation() {
        var func = Compile<long>("NOT a = 1", "a");
        Assert.True(func(2));
        Assert.False(func(1));
    }

    [Fact]
    public void Build_CustomFunction_MethodCall() {
        var filter = Parser.Filter.Parse("(msg.endsWith('world') AND retries < 10)");
        Assert.NotNull(filter);
        var expr = Container.Build(filter)
                            .RegisterFunction("endsWith", (args, ctx) => {
                                 var method = ctx.GetMethod(typeof(string), "EndsWith", [typeof(string)]);
                                 return Expression.Call(args[0], method!, args[1]);
                             })
                            .Bind("msg", typeof(string))
                            .Bind("retries", typeof(int))
                            .Build();
        var func = (Func<string, int, bool>)expr!.Compile();
        Assert.True(func("hello world", 9));
        Assert.False(func("hello world", 10));
    }

    [Fact]
    public void Build_StandaloneFunction_StartsWith() {
        var filter = Parser.Filter.Parse("startsWith(name, 'hel')");
        Assert.NotNull(filter);
        var expr = Container.Build(filter)
                            .RegisterFunction("startsWith", (args, ctx) => {
                                 var method = ctx.GetMethod(typeof(string), "StartsWith", [typeof(string)]);
                                 return Expression.Call(args[0], method!, args[1]);
                             })
                            .Bind("name", typeof(string))
                            .Build();
        var func = (Func<string, bool>)expr!.Compile();
        Assert.True(func("hello world"));
        Assert.False(func("goodbye hello"));
    }

    [Fact]
    public void Build_KeywordAsField_AccessesProperty() {
        var filter = Parser.Filter.Parse("obj.and = 'test'");
        Assert.NotNull(filter);
        var expr = Container.Build(filter).Bind("obj", typeof(KeywordEntity)).Build();
        var func = (Func<KeywordEntity, bool>)expr!.Compile();
        Assert.True(func(new() { and  = "test" }));
        Assert.False(func(new() { and = "other" }));
    }

    private static Func<T, bool> Compile<T>(string filterExpr, string bindName) {
        var filter = Parser.Filter.Parse(filterExpr);
        Assert.NotNull(filter);
        var expr = Container.Build(filter).Bind(bindName, typeof(T)).Build();
        Assert.NotNull(expr);
        return (Func<T, bool>)expr.Compile();
    }

    #region Test types

    // ReSharper disable InconsistentNaming

    public class KeywordEntity
    {
        public string and { get; set; } = string.Empty;
    }

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

    // ReSharper restore InconsistentNaming

    #endregion
}

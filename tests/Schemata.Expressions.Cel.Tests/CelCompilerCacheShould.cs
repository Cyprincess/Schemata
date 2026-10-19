using System.Collections.Generic;
using Parlot;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelCompilerCacheShould
{
    private readonly CelCompiler _compiler = new();

    [Fact]
    public void Compile_Parser_Direct_Trees_With_Empty_Source_Compile_To_Distinct_Predicates() {
        var first  = Assert.IsAssignableFrom<CelNode>(CelParser.Expression.Parse("age == 18"));
        var second = Assert.IsAssignableFrom<CelNode>(CelParser.Expression.Parse("age == 19"));

        Assert.Equal(string.Empty, first.Source);
        Assert.Equal(string.Empty, second.Source);

        var firstFunc  = _compiler.Compile<Row, bool>(first).Compile();
        var secondFunc = _compiler.Compile<Row, bool>(second).Compile();

        Assert.True(firstFunc(new() { Age  = 18 }));
        Assert.False(firstFunc(new() { Age = 19 }));
        Assert.False(secondFunc(new() { Age = 18 }));
        Assert.True(secondFunc(new() { Age = 19 }));
    }

    [Fact]
    public void Compile_Same_Tree_Returns_Cached_Expression_Instance() {
        var tree   = _compiler.Parse("age == 18");
        var first  = _compiler.Compile<Row, bool>(tree);
        var second = _compiler.Compile<Row, bool>(tree);

        Assert.Same(first, second);
    }

    [Fact]
    public void Compile_AbsentAlias_And_LiteralNoneAlias_SelectDifferentBindings() {
        var tree    = _compiler.Parse("none.name");
        var context = new Dictionary<string, object?> {
            ["name"] = "Root",
            ["none"] = new Dictionary<string, object?> { ["name"] = "Nested" },
        };

        var withoutAlias = _compiler.Compile<IReadOnlyDictionary<string, object?>, object>(tree).Compile();
        var withAlias    = _compiler.Compile<IReadOnlyDictionary<string, object?>, object>(
            tree, new ExpressionCompileOptions { ContextAlias = "none" }).Compile();

        Assert.Equal("Nested", withoutAlias(context));
        Assert.Equal("Root", withAlias(context));
    }

    [Fact]
    public void Compile_Equal_Source_Annotations_Do_Not_Unify_Distinct_Trees() {
        var first  = Assert.IsAssignableFrom<CelNode>(CelParser.Expression.Parse("age == 18"));
        var second = Assert.IsAssignableFrom<CelNode>(CelParser.Expression.Parse("age == 19"));
        second.Source = "age == 18";

        var firstFunc  = _compiler.Compile<Row, bool>(first).Compile();
        var secondFunc = _compiler.Compile<Row, bool>(second).Compile();

        Assert.True(firstFunc(new() { Age  = 18 }));
        Assert.False(firstFunc(new() { Age = 19 }));
        Assert.True(secondFunc(new() { Age  = 19 }));
        Assert.False(secondFunc(new() { Age = 18 }));
    }

    [Fact]
    public void Compile_Mutated_Source_Annotation_Keeps_Original_Semantics() {
        var tree = Assert.IsAssignableFrom<CelNode>(_compiler.Parse("age == 18"));
        Assert.True(_compiler.Compile<Row, bool>(tree).Compile()(new() { Age = 18 }));

        // Source is a caller-writable annotation; rewriting it must not redirect compilation.
        tree.Source = "age == 19";
        var func = _compiler.Compile<Row, bool>(tree).Compile();
        Assert.True(func(new() { Age  = 18 }));
        Assert.False(func(new() { Age = 19 }));
    }

    [Fact]
    public void Compile_Mutated_Child_List_Recompiles_To_New_Semantics() {
        var items = new List<CelNode> { new CelConstant(1L) };
        var tree  = new CelBinary(">", new CelCall("size", new List<CelNode> { new CelList(items) }), new CelConstant(1L));

        Assert.False(_compiler.Compile<Row, bool>(tree).Compile()(new Row()));

        items.Add(new CelConstant(2L));
        Assert.True(_compiler.Compile<Row, bool>(tree).Compile()(new Row()));
    }

    [Fact]
    public void Compile_CustomConstants_PreserveEachCapturedInstance() {
        var first = new ConsumerConstant();
        var second = new ConsumerConstant();
        var firstFunc = _compiler.Compile<Row, object>(new CelConstant(first)).Compile();
        var secondFunc = _compiler.Compile<Row, object>(new CelConstant(second)).Compile();

        Assert.Same(first, firstFunc(new Row()));
        Assert.Same(second, secondFunc(new Row()));
    }

    [Fact]
    public void Compile_EqualMutableByteConstants_PreserveIndependentMutations() {
        byte[] first = [1, 2];
        byte[] second = [1, 2];
        var firstFunc = _compiler.Compile<Row, byte[]>(new CelConstant(first)).Compile();
        var secondFunc = _compiler.Compile<Row, byte[]>(new CelConstant(second)).Compile();

        first[0] = 9;
        Assert.Equal(new byte[] { 9, 2 }, firstFunc(new Row()));
        Assert.Equal(new byte[] { 1, 2 }, secondFunc(new Row()));
        second[1] = 8;
        Assert.Equal(new byte[] { 9, 2 }, firstFunc(new Row()));
        Assert.Equal(new byte[] { 1, 8 }, secondFunc(new Row()));
    }

    [Fact]
    public void Compile_UnsupportedConsumerNode_NormalizesParseException() {
        var exception = Assert.Throws<ExpressionException>(() => _compiler.Compile<Row, object>(new UnsupportedNode()));

        Assert.IsType<ParseException>(exception.InnerException);
    }

    private sealed class ConsumerConstant
    {
        public override string ToString() => throw new System.InvalidOperationException("Consumer constants need not support stringification.");
    }

    private sealed class UnsupportedNode : CelNode { }

    private sealed class Row
    {
        public int Age { get; set; }
    }
}

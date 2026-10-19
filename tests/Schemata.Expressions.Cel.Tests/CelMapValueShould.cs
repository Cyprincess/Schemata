using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelMapValueShould
{
    private readonly CelCompiler _compiler = new();

    private object? Evaluate(string source, Dictionary<string, object?> bindings) {
        var tree = _compiler.Parse(source);
        var func = _compiler.Compile<Dictionary<string, object?>, object?>(tree).Compile();
        return func(bindings);
    }

    private bool EvaluateBool(string source, Dictionary<string, object?> bindings) {
        var tree = _compiler.Parse(source);
        var func = _compiler.Compile<Dictionary<string, object?>, bool>(tree).Compile();
        return func(bindings);
    }

    private static Dictionary<string, object?> Labels() {
        return new() {
            ["labels"] = new Dictionary<string, object?> { ["owner"] = "ops", ["team"] = "dev" },
        };
    }

    [Fact]
    public void Evaluate_BoundStringKeyedMap_IndexesLikeLiteralMap() {
        var bound = Labels();

        Assert.Equal("ops", Evaluate("labels['owner']", bound));
        Assert.Equal("ops", Evaluate("{'owner': 'ops'}['owner']", new()));
    }

    [Fact]
    public void Evaluate_BoundStringKeyedMap_SupportsSizeAndMembership() {
        var bound = Labels();

        Assert.Equal(2L, Evaluate("size(labels)", bound));
        Assert.True(EvaluateBool("'owner' in labels", bound));
        Assert.False(EvaluateBool("'missing' in labels", bound));
    }

    [Fact]
    public void Evaluate_BoundStringKeyedMap_SupportsMapMacros() {
        var bound = Labels();

        Assert.True(EvaluateBool("labels.exists(k, v, k == 'owner' && v == 'ops')", bound));
        Assert.True(EvaluateBool("labels.map(k, k).contains('team')", bound));
        Assert.Equal(2L, Evaluate("labels.transformList(k, v, v).size()", bound));
    }

    [Fact]
    public void Evaluate_ReadOnlyObjectKeyedMap_MemberMatchesIndex() {
        var bound = new Dictionary<string, object?> {
            ["m"] = new ObjectKeyedMap(new Dictionary<object, object?> { ["name"] = "Alice" }),
        };

        Assert.Equal("Alice", Evaluate("m.name", bound));
        Assert.Equal(Evaluate("m['name']", bound), Evaluate("m.name", bound));
        Assert.True(EvaluateBool("has(m.name)", bound));
        Assert.False(EvaluateBool("has(m.missing)", bound));
    }

    [Fact]
    public void Evaluate_NumericMapKeys_LookUpAcrossIntegerAndDoubleRepresentations() {
        Assert.True(EvaluateBool("dyn(1) == 1.0", new()));
        Assert.Equal("one", Evaluate("{1: 'one'}[1.0]", new()));
        Assert.Equal("one", Evaluate("{1.0: 'one'}[1]", new()));
        Assert.True(EvaluateBool("1.0 in {1: 'one'}", new()));
        Assert.True(EvaluateBool("1 in {1.0: 'one'}", new()));
    }

    [Theory]
    [InlineData("{-9223372036854775808: 'minimum'}[-9223372036854775808.0]")]
    [InlineData("{-9223372036854775808.0: 'minimum'}[-9223372036854775808]")]
    public void Evaluate_NumericMapKeys_RejectDoubleIntMinCrossRepresentationLookup(string source) {
        Assert.IsType<CelError>(Evaluate(source, new()));
    }

    [Fact]
    public void Evaluate_NumericMapKeys_IntMinRetainsSeparateRepresentations() {
        Assert.Equal(2L, Evaluate("size({-9223372036854775808: 'integer', -9223372036854775808.0: 'double'})", new()));
        Assert.Equal("integer", Evaluate("{-9223372036854775808: 'integer', -9223372036854775808.0: 'double'}[-9223372036854775808]", new()));
        Assert.Equal("double", Evaluate("{-9223372036854775808: 'integer', -9223372036854775808.0: 'double'}[-9223372036854775808.0]", new()));
    }

    [Fact]
    public void Evaluate_NumericMapKeys_EqualKeysCollapseToSingleEntry() {
        Assert.Equal(1L, Evaluate("size({1: 'one', 1.0: 'uno'})", new()));
        Assert.Equal("uno", Evaluate("{1: 'one', 1.0: 'uno'}[1]", new()));
    }

    [Fact]
    public void Evaluate_LargeIntegralMapKeys_StayDistinct() {
        Assert.Equal(2L, Evaluate("size({9007199254740993: 'a', 9007199254740992.0: 'b'})", new()));
        Assert.Equal("a", Evaluate("{9007199254740993: 'a', 9007199254740992.0: 'b'}[9007199254740993]", new()));
        Assert.Equal("b", Evaluate("{9007199254740993: 'a', 9007199254740992.0: 'b'}[9007199254740992.0]", new()));
        Assert.Equal(2L, Evaluate("size({9007199254740993: 'a', 9007199254740992: 'b'})", new()));
        Assert.Equal("a", Evaluate("{9007199254740993: 'a', 9007199254740992: 'b'}[9007199254740993]", new()));
        Assert.Equal("b", Evaluate("{9007199254740993: 'a', 9007199254740992: 'b'}[9007199254740992]", new()));
    }

    [Fact]
    public void Evaluate_StructuralMapKeys_LookUpAcrossEqualContent() {
        Assert.Equal("yes", Evaluate("{[1, 2]: 'yes'}[[1, 2]]", new()));
        Assert.Equal("yes", Evaluate("{{'a': 1}: 'yes'}[{'a': 1.0}]", new()));
        Assert.Equal(2L, Evaluate("size({[9007199254740993]: 'a', [9007199254740992.0]: 'b'})", new()));
        Assert.Equal("a", Evaluate("{[9007199254740993]: 'a', [9007199254740992.0]: 'b'}[[9007199254740993]]", new()));
        Assert.Equal("b", Evaluate("{[9007199254740993]: 'a', [9007199254740992.0]: 'b'}[[9007199254740992.0]]", new()));
    }

    [Theory]
    [InlineData("9007199254740993 == 9007199254740992.0", true)]
    [InlineData("9007199254740993 > 9007199254740992.0", false)]
    [InlineData("9007199254740993 < 9007199254740992.0", false)]
    [InlineData("9007199254740993 == 9007199254740993", true)]
    [InlineData("-9223372036854775808 < 9223372036854775808u", true)]
    [InlineData("9223372036854775807u < 9223372036854775808.0", false)]
    [InlineData("18446744073709551615u > 9223372036854775807", true)]
    [InlineData("18446744073709551615u < 18446744073709551616.0", false)]
    [InlineData("dyn(1) == 1.0", true)]
    [InlineData("dyn(1u) == 1.0", true)]
    [InlineData("9007199254740993 > 9007199254740992", true)]
    [InlineData("dyn(9223372036854775807) < 9223372036854775808.0", false)]
    [InlineData("dyn(9223372036854775807) >= 9223372036854775808.0", true)]
    [InlineData("dyn(9223372036854775808.0) > 9223372036854775807", false)]
    [InlineData("dyn(9223372036854775808.0) <= 9223372036854775807", true)]
    [InlineData("dyn(9223372036854775807) == 9223372036854775808.0", true)]
    [InlineData("dyn(9223372036854775808.0) == 9223372036854775807", true)]
    [InlineData("9007199254740992.0 == 9007199254740993", true)]
    [InlineData("9007199254740992.0 >= 9007199254740993", true)]
    [InlineData("dyn(9223372036854777857.0) > 9223372036854775807", true)]
    [InlineData("dyn(18446744073709553665.0) > 18446744073709551615u", true)]
    public void Evaluate_MixedNumericComparison_FollowsCelReference(string source, bool expected) {
        Assert.Equal(expected, EvaluateBool(source, new()));
    }

    #region Nested type: ObjectKeyedMap

    private sealed class ObjectKeyedMap(IReadOnlyDictionary<object, object?> map) : IReadOnlyDictionary<object, object?>
    {
        public object? this[object key] => map[key];

        public IEnumerable<object> Keys => map.Keys;

        public IEnumerable<object?> Values => map.Values;

        public int Count => map.Count;

        public bool ContainsKey(object key) { return map.ContainsKey(key); }

        public bool TryGetValue(object key, out object? value) { return map.TryGetValue(key, out value); }

        public IEnumerator<KeyValuePair<object, object?>> GetEnumerator() { return map.GetEnumerator(); }

        IEnumerator IEnumerable.GetEnumerator() { return map.GetEnumerator(); }
    }

    #endregion
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelCompileVisitorShould
{
    private static readonly MethodInfo ShoutMethod = typeof(CelCompileVisitorShould)
        .GetMethod(nameof(Shout), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly CelCompiler _compiler = new();

    [Theory]
    [InlineData("4294967296 == big_amount")]
    [InlineData("big_amount == 4294967296")]
    public void Compile_EqualityBeyondIntRange_IsOrderIndependent(string source) {
        var func = _compiler.Compile<NumbersRow, bool>(_compiler.Parse(source)).Compile();

        Assert.True(func(new() { BigAmount = 4294967296L }));
        Assert.False(func(new() { BigAmount = 7L }));
    }

    [Theory]
    [InlineData("2147483648 + amount", 2147483649L)]
    [InlineData("amount + 2147483648", 2147483649L)]
    public void Compile_IntAndLongArithmetic_PromotesToLong(string source, long expected) {
        var func = _compiler.Compile<NumbersRow, long>(_compiler.Parse(source)).Compile();

        Assert.Equal(expected, func(new() { Amount = 1 }));
    }

    [Fact]
    public void Compile_UIntAndIntOperands_PromoteToLong() {
        var func = _compiler.Compile<NumbersRow, long>(_compiler.Parse("quantity + amount")).Compile();

        Assert.Equal(4294967298L, func(new() { Quantity = 4294967295U, Amount = 3 }));
    }

    [Fact]
    public void Compile_UIntAndULongOperands_PromoteToULong() {
        var func = _compiler.Compile<NumbersRow, ulong>(_compiler.Parse("quantity + total")).Compile();

        Assert.Equal(3UL, func(new() { Quantity = 1U, Total = 2UL }));
    }

    [Theory]
    [InlineData("total + big_amount")]
    [InlineData("big_amount + total")]
    public void Compile_ULongAndLongOperands_RejectLackOfCommonRepresentation(string source) {
        var tree = _compiler.Parse(source);

        Assert.Throws<ExpressionException>(() => _compiler.Compile<NumbersRow, ulong>(tree));
    }

    [Theory]
    [InlineData("small + other_small", 60000)]
    [InlineData("other_small + small", 60000)]
    [InlineData("octet + other_octet", 400)]
    [InlineData("other_octet + octet", 400)]
    [InlineData("small + octet", 30200)]
    [InlineData("octet + small", 30200)]
    public void Compile_SmallIntegralArithmetic_PromotesWithoutOverflow(string source, int expected) {
        var func = _compiler.Compile<NumbersRow, int>(_compiler.Parse(source)).Compile();

        Assert.Equal(expected, func(new() { Small = 30000, OtherSmall = 30000, Octet = 200, OtherOctet = 200 }));
    }

    [Fact]
    public void Compile_CustomFunction_BindsInValueMode() {
        var options = new ExpressionCompileOptions {
            Functions = { ["shout"] = new(args => Expression.Call(ShoutMethod, args[0])) },
        };
        var func = _compiler.Compile<object, object>(_compiler.Parse("shout(name)"), options).Compile();

        Assert.Equal("ALICE", func(new Dictionary<string, object?> { ["name"] = "alice" }));
    }

    [Fact]
    public void Compile_ContextAlias_BindsBareIdentifierToContextInValueMode() {
        var options = new ExpressionCompileOptions { ContextAlias = "s" };
        var func    = _compiler.Compile<IReadOnlyDictionary<string, object?>, object>(_compiler.Parse("s"), options)
                                .Compile();
        var row = new Dictionary<string, object?> { ["name"] = "alice" };

        Assert.Same(row, func(row));
    }

    [Fact]
    public void Compile_ContextAlias_ResolvesQualifiedMembersInValueMode() {
        var options = new ExpressionCompileOptions { ContextAlias = "s" };
        var func    = _compiler
                     .Compile<IReadOnlyDictionary<string, object?>, bool>(_compiler.Parse("s.name == 'alice'"), options)
                     .Compile();

        Assert.True(func(new Dictionary<string, object?> { ["name"] = "alice" }));
        Assert.False(func(new Dictionary<string, object?> { ["name"] = "bob" }));
    }

    [Fact]
    public void Compile_ContextAlias_ResolvesQualifiedMembersInTypedMode() {
        var options = new ExpressionCompileOptions { ContextAlias = "s" };
        var func    = _compiler.Compile<Profile, bool>(_compiler.Parse("s.full_name == 'Alice'"), options).Compile();

        Assert.True(func(new() { FullName = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public void Compile_FilteredMapWithBareIdentifierPredicate_FiltersThroughPredicateField(bool enabled, int expected) {
        var func = _compiler
                  .Compile<IReadOnlyDictionary<string, object?>, object>(_compiler.Parse("items.map(x, enabled, x)"))
                  .Compile();
        var items = new object?[] {
            new Dictionary<string, object?> { ["v"] = 1L },
            new Dictionary<string, object?> { ["v"] = 2L },
        };

        var result = Assert.IsType<List<object?>>(func(new Dictionary<string, object?> {
            ["enabled"] = enabled,
            ["items"]   = items,
        }));

        Assert.Equal(expected, result.Count);
    }

    [Theory]
    [InlineData("amount == null", false)]
    [InlineData("amount != null", true)]
    [InlineData("null == null", true)]
    public void Compile_NullComparison_AgainstNonNullableValue_DecidesStatically(string source, bool expected) {
        var func = _compiler.Compile<NumbersRow, bool>(_compiler.Parse(source)).Compile();

        Assert.Equal(expected, func(new() { Amount = 7 }));
    }

    [Theory]
    [InlineData("amount < null")]
    [InlineData("null >= amount")]
    [InlineData("null < null")]
    public void Compile_OrderingAgainstNull_RejectsAtCompileTime(string source) {
        var tree = _compiler.Parse(source);

        Assert.Throws<ExpressionException>(() => _compiler.Compile<NumbersRow, bool>(tree));
    }

    [Fact]
    public void Compile_NullComparison_AgainstNullableValue_KeepsNullSemantics() {
        var func = _compiler.Compile<NullableRow, bool>(_compiler.Parse("amount == null")).Compile();

        Assert.True(func(new() { Amount = null }));
        Assert.False(func(new() { Amount = 7 }));
    }

    [Theory]
    [InlineData("'a' + 'b'")]
    [InlineData("first + second")]
    public void Compile_StringAddition_ConcatsInTypedMode(string source) {
        var func = _compiler.Compile<TextRow, string>(_compiler.Parse(source)).Compile();

        Assert.Equal("ab", func(new() { First = "a", Second = "b" }));
    }

    [Fact]
    public void Compile_NullComparison_TypedMode_MatchesValueMode() {
        var typed  = _compiler.Compile<NumbersRow, bool>(_compiler.Parse("amount == null")).Compile();
        var valued = _compiler
                    .Compile<IReadOnlyDictionary<string, object?>, object>(_compiler.Parse("amount == null"))
                    .Compile();
        var row = new Dictionary<string, object?> { ["amount"] = 1L };

        Assert.False(typed(new() { Amount = 1 }));
        Assert.Equal(false, valued(row));
    }

    [Fact]
    public void Compile_StringAddition_TypedMode_MatchesValueMode() {
        var typed  = _compiler.Compile<TextRow, string>(_compiler.Parse("first + second")).Compile();
        var valued = _compiler
                    .Compile<IReadOnlyDictionary<string, object?>, object>(_compiler.Parse("first + second"))
                    .Compile();
        var row = new Dictionary<string, object?> { ["first"] = "a", ["second"] = "b" };

        Assert.Equal("ab", typed(new() { First = "a", Second = "b" }));
        Assert.Equal("ab", valued(row));
    }

    [Fact]
    public void Compile_InOperator_ResolvesContainsKeyThroughExplicitInterface() {
        var func = _compiler.Compile<LabelRow, bool>(_compiler.Parse("'a' in labels")).Compile();

        Assert.True(func(new() { Labels = new(["a", "b"]) }));
        Assert.False(func(new() { Labels = new(["b"]) }));
    }

    private static string? Shout(object? value) {
        return value is string text ? text.ToUpperInvariant() : null;
    }

    #region Nested type: NumbersRow

    private sealed class NumbersRow
    {
        public int Amount { get; set; }

        public uint Quantity { get; set; }

        public long BigAmount { get; set; }

        public ulong Total { get; set; }

        public short Small { get; set; }

        public short OtherSmall { get; set; }

        public byte Octet { get; set; }

        public byte OtherOctet { get; set; }
    }

    #endregion

    #region Nested type: Profile

    private sealed class Profile
    {
        public string? FullName { get; set; }
    }

    #endregion

    #region Nested type: LabelRow

    private sealed class LabelRow
    {
        public LabelMap Labels { get; set; } = new([]);
    }

    #endregion

    #region Nested type: TextRow

    private sealed class TextRow
    {
        public string First { get; set; } = string.Empty;

        public string Second { get; set; } = string.Empty;
    }

    #endregion

    #region Nested type: NullableRow

    private sealed class NullableRow
    {
        public int? Amount { get; set; }
    }

    #endregion

    #region Nested type: LabelMap

    private sealed class LabelMap(string[] keys) : IReadOnlyDictionary<string, object?>
    {
        private readonly IReadOnlyDictionary<string, object?> _map = keys.ToDictionary(key => key, key => (object?)key);

        bool IReadOnlyDictionary<string, object?>.ContainsKey(string key) {
            return _map.ContainsKey(key);
        }

        bool IReadOnlyDictionary<string, object?>.TryGetValue(string key, out object? value) {
            return _map.TryGetValue(key, out value);
        }

        IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => _map.Keys;

        IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => _map.Values;

        int IReadOnlyCollection<KeyValuePair<string, object?>>.Count => _map.Count;

        object? IReadOnlyDictionary<string, object?>.this[string key] => _map[key];

        IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() {
            return _map.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return _map.GetEnumerator();
        }
    }

    #endregion
}

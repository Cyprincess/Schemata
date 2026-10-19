using System;
using System.Collections.Generic;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Aip.Tests.Compilation;

public class AipDynamicEvaluationShould
{
    private readonly AipCompiler _compiler = new();

    [Fact]
    public void Evaluate_Equality_Over_Nested_Row() {
        var predicate = Predicate("o.status = 'paid'");

        Assert.True(predicate(Row("o", ("status", "paid"))));
        Assert.False(predicate(Row("o", ("status", "open"))));
    }

    [Fact]
    public void Treat_Missing_Field_As_NonMatch() {
        var predicate = Predicate("o.amount > 5");

        Assert.False(predicate(Row("o", ("other", 1))));
    }

    [Fact]
    public void Evaluate_Numeric_Ordering_Across_Numeric_Types() {
        var predicate = Predicate("o.amount > 5");

        Assert.True(predicate(Row("o", ("amount", 10L))));
        Assert.False(predicate(Row("o", ("amount", 3))));
    }

    [Fact]
    public void Compose_Conjunction_And_Disjunction() {
        var conjunction = Predicate("o.amount > 5 AND o.status = 'paid'");
        Assert.True(conjunction(Row("o", ("amount", 10), ("status", "paid"))));
        Assert.False(conjunction(Row("o", ("amount", 10), ("status", "open"))));

        var disjunction = Predicate("o.status = 'paid' OR o.status = 'shipped'");
        Assert.True(disjunction(Row("o", ("status", "shipped"))));
        Assert.False(disjunction(Row("o", ("status", "open"))));
    }

    [Fact]
    public void Treat_Null_Leaf_As_Distinct_From_Literal() {
        var predicate = Predicate("o.status != 'paid'");

        Assert.True(predicate(Row("o", ("status", null))));
        Assert.False(predicate(Row("o", ("status", "paid"))));
    }

    [Fact]
    public void Evaluate_Cross_Source_Equality_Over_Combined_Row() {
        var tree      = _compiler.Parse("c.id = o.customer_id");
        var predicate = _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree).Compile();

        Assert.True(predicate(Combined(("c", "id", 1), ("o", "customer_id", 1))));
        Assert.False(predicate(Combined(("c", "id", 1), ("o", "customer_id", 2))));
    }

    [Fact]
    public void Evaluate_Presence_As_Present_And_NonNull() {
        var predicate = Predicate("o.email : *");

        Assert.True(predicate(Row("o", ("email", "a@b.com"))));
        Assert.False(predicate(Row("o", ("email", null))));
        Assert.False(predicate(Row("o", ("other", 1))));
    }

    [Fact]
    public void Reject_Function_In_Dynamic_Evaluation() {
        var tree = _compiler.Parse("regex(o.name, 'a')");

        Assert.Throws<ExpressionException>(() => _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree));
    }

    [Fact]
    public void Evaluate_Undotted_Field_Reads_Row_Value() {
        var predicate = Predicate("age > 18");

        Assert.True(predicate(Flat(("age", 20))));
        Assert.False(predicate(Flat(("age", 18))));
    }

    [Fact]
    public void Evaluate_Undotted_Field_On_Either_Side_Reads_Row_Value() {
        var predicate = Predicate("18 < age");

        Assert.True(predicate(Flat(("age", 20))));
        Assert.False(predicate(Flat(("age", 18))));
    }

    [Fact]
    public void Evaluate_Quoted_Operand_Stays_A_String_Literal() {
        var predicate = Predicate("name = 'age'");

        Assert.True(predicate(Flat(("name", "age"), ("age", 20))));
        Assert.False(predicate(Flat(("name", "Alice"))));
    }

    [Fact]
    public void Evaluate_Adjacent_Longs_Beyond_Double_Precision_Stay_Distinct() {
        var equal = Predicate("v = 9007199254740992");
        Assert.True(equal(Flat(("v", 9007199254740992L))));
        Assert.False(equal(Flat(("v", 9007199254740993L))));

        var above = Predicate("v > 9007199254740992");
        Assert.True(above(Flat(("v", 9007199254740993L))));
        Assert.False(above(Flat(("v", 9007199254740992L))));

        var below = Predicate("v < 9007199254740993");
        Assert.True(below(Flat(("v", 9007199254740992L))));
        Assert.False(below(Flat(("v", 9007199254740993L))));
    }

    [Fact]
    public void Evaluate_Signed_Unsigned_Boundaries_Order_Correctly() {
        var row = Flat(("a", long.MinValue), ("b", ulong.MaxValue));

        Assert.True(Predicate("a < b")(row));
        Assert.True(Predicate("b > a")(row));
    }

    [Fact]
    public void Evaluate_Decimal_Precision_Is_Retained() {
        var predicate = Predicate("m = 0.1");

        Assert.True(predicate(Flat(("m", 0.1m))));
        Assert.False(predicate(Flat(("m", 0.1000000000000000000000000001m))));
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> Predicate(string source) {
        var tree = _compiler.Parse(source);
        return _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree).Compile();
    }

    private static IReadOnlyDictionary<string, object?> Flat(params (string Key, object? Value)[] fields) {
        var row = new Dictionary<string, object?>();
        foreach (var (key, value) in fields) {
            row[key] = value;
        }

        return row;
    }

    private static IReadOnlyDictionary<string, object?> Row(string alias, params (string Key, object? Value)[] fields) {
        var inner = new Dictionary<string, object?>();
        foreach (var (key, value) in fields) {
            inner[key] = value;
        }

        return new Dictionary<string, object?> { [alias] = inner };
    }

    private static IReadOnlyDictionary<string, object?> Combined(params (string Alias, string Key, object? Value)[] cells) {
        var row = new Dictionary<string, object?>();
        foreach (var (alias, key, value) in cells) {
            if (row.TryGetValue(alias, out var existing) && existing is Dictionary<string, object?> inner) {
                inner[key] = value;
                continue;
            }

            row[alias] = new Dictionary<string, object?> { [key] = value };
        }

        return row;
    }
}

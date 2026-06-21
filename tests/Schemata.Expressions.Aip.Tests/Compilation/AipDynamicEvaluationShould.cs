using System;
using System.Collections.Generic;
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

        Assert.ThrowsAny<Exception>(() => _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree));
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> Predicate(string source) {
        var tree = _compiler.Parse(source);
        return _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree).Compile();
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

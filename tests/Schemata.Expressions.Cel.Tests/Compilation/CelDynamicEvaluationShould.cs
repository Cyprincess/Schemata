using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Schemata.Expressions.Cel.Tests.Compilation;

public class CelDynamicEvaluationShould
{
    private readonly CelCompiler _compiler = new();

    [Fact]
    public void Evaluate_Equality_Over_Nested_Row() {
        var predicate = Predicate("o.status == 'paid'");

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
    public void Compose_Conjunction_Disjunction_And_Negation() {
        var predicate = Predicate("!o.cancelled && (o.status == 'paid' || o.amount > 100)");

        Assert.True(predicate(Row("o", ("cancelled", false), ("status", "paid"), ("amount", 20))));
        Assert.True(predicate(Row("o", ("cancelled", false), ("status", "open"), ("amount", 120))));
        Assert.False(predicate(Row("o", ("cancelled", true), ("status", "paid"), ("amount", 120))));
        Assert.False(predicate(Row("o", ("cancelled", false), ("status", "open"), ("amount", 20))));
    }

    [Fact]
    public void Evaluate_Arithmetic_Over_Nested_Row() {
        var tree    = _compiler.Parse("c.discount * o.amount");
        var compute = _compiler.Compile<IReadOnlyDictionary<string, object?>, object>(tree).Compile();

        Assert.Equal(new CelError("no matching overload"), compute(Combined(("c", "discount", 0.2), ("o", "amount", 100))));
    }

    [Trait("Layer", "Unit")]
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false, "missing", "undeclared reference to 'missing' (in container '')")]
    [InlineData(true, "missing", "undeclared reference to 'missing' (in container '')")]
    [InlineData(false, "numerator / denominator", "divide by zero")]
    [InlineData(true, "numerator / denominator", "divide by zero")]
    public void Return_Standalone_Error_Values_With_Message_Equality_And_Json(bool objectContext, string source, string message) {
        var tree = _compiler.Parse(source);
        IReadOnlyDictionary<string, object?> input = new Dictionary<string, object?> { ["numerator"] = 42L, ["denominator"] = 0L };
        var result = objectContext
            ? _compiler.Compile<object, object>(tree).Compile()(input)
            : _compiler.Compile<IReadOnlyDictionary<string, object?>, object>(tree).Compile()(input);

        var error = Assert.IsType<CelError>(result);
        Assert.Equal(new CelError(message), error);
        var json = JsonSerializer.Serialize(error);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Message", Assert.Single(document.RootElement.EnumerateObject()).Name);
        Assert.Equal(message, document.RootElement.GetProperty("Message").GetString());
        Assert.Equal(error, JsonSerializer.Deserialize<CelError>(json));
    }

    [Fact]
    public void Evaluate_Presence_As_Present_And_NonNull() {
        var predicate = Predicate("has(o.email)");

        Assert.True(predicate(Row("o", ("email", "a@b.com"))));
        Assert.True(predicate(Row("o", ("email", null))));
        Assert.False(predicate(Row("o", ("other", 1))));
    }

    [Fact]
    public void Evaluate_Cross_Source_Equality_Over_Combined_Row() {
        var predicate = Predicate("c.id == o.customer_id");

        Assert.True(predicate(Combined(("c", "id", 1), ("o", "customer_id", 1))));
        Assert.False(predicate(Combined(("c", "id", 1), ("o", "customer_id", 2))));
    }

    [Fact]
    public void Evaluate_Macros_In_Dynamic_Evaluation() {
        var tree = _compiler.Parse("o.tags.exists(x, x == 1)");
        var func = _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(tree).Compile();

        Assert.True(func(Row("o", ("tags", new object?[] { 0, 1, 2 }))));
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

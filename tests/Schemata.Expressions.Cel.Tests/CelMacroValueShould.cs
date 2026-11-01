using System.Collections.Generic;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelMacroValueShould
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

    private static Dictionary<string, object?> WithScalarSource() {
        return new() {
            ["score"] = 1L,
            ["name"]  = "alice",
        };
    }

    private static Dictionary<string, object?> WithListSource() {
        return new() { ["scores"] = new List<object?> { 1L, 2L, 3L } };
    }

    [Theory]
    [InlineData("score.exists(x, true)")]
    [InlineData("score.all(x, true)")]
    [InlineData("score.filter(x, true)")]
    [InlineData("score.map(x, 1)")]
    [InlineData("score.existsOne(x, true)")]
    [InlineData("score.exists(i, v, true)")]
    [InlineData("score.all(i, v, true)")]
    [InlineData("score.existsOne(i, v, true)")]
    [InlineData("score.transformList(i, v, 7)")]
    [InlineData("score.transformMap(i, v, 7)")]
    [InlineData("name.exists(c, true)")]
    public void Evaluate_MacroOnUnsupportedSource_ReturnsErrorInsteadOfSuccess(string source) {
        Assert.IsType<CelError>(Evaluate(source, WithScalarSource()));
    }

    [Theory]
    [InlineData("scores.exists(s, 7)")]
    [InlineData("scores.all(s, 7)")]
    [InlineData("scores.filter(s, 7)")]
    [InlineData("scores.existsOne(s, 7)")]
    [InlineData("scores.map(s, 7, s * 2)")]
    [InlineData("scores.exists(i, v, 7)")]
    [InlineData("scores.all(i, v, 7)")]
    [InlineData("scores.existsOne(i, v, 7)")]
    [InlineData("scores.transformList(i, v, 7, v)")]
    [InlineData("scores.transformMap(i, v, 7, v)")]
    public void Evaluate_MacroWithNonBooleanPredicate_ReturnsError(string source) {
        Assert.IsType<CelError>(Evaluate(source, WithListSource()));
    }

    [Theory]
    [InlineData("scores.exists(s, s == 2)", true)]
    [InlineData("scores.exists(s, s == 9)", false)]
    [InlineData("scores.all(s, s > 0)", true)]
    [InlineData("scores.all(s, s > 1)", false)]
    [InlineData("scores.existsOne(s, s == 2)", true)]
    [InlineData("scores.existsOne(s, s > 0)", false)]
    [InlineData("scores.exists(i, v, v == 2 && i == 1)", true)]
    [InlineData("scores.all(i, v, v > 0)", true)]
    [InlineData("scores.existsOne(i, v, v == 2)", true)]
    public void Evaluate_MacroOnListSource_KeepsBooleanSemantics(string source, bool expected) {
        Assert.Equal(expected, EvaluateBool(source, WithListSource()));
    }

    [Theory]
    [InlineData("labels.exists(k, v, k == 'owner')", true)]
    [InlineData("labels.exists(k, v, k == 'guest')", false)]
    [InlineData("labels.all(k, v, v == 'ops' || v == 'dev')", true)]
    public void Evaluate_MacroOnMapSource_KeepsEntrySemantics(string source, bool expected) {
        var bindings = new Dictionary<string, object?> {
            ["labels"] = new Dictionary<string, object?> { ["owner"] = "ops", ["team"] = "dev" },
        };

        Assert.Equal(expected, EvaluateBool(source, bindings));
    }

    [Fact]
    public void Evaluate_ExistsWithDecisiveTrue_DominatesPredicateError() {
        var bindings = new Dictionary<string, object?> { ["scores"] = new List<object?> { 1L, 0L } };

        Assert.Equal(true, Evaluate("scores.exists(s, 1 / s == 1)", bindings));
    }

    [Fact]
    public void Evaluate_MacroPredicateErrors_StillPropagate() {
        var bindings = new Dictionary<string, object?> { ["scores"] = new List<object?> { 1L, 0L } };

        Assert.IsType<CelError>(Evaluate("scores.all(s, 1 / s == 1)", bindings));
        Assert.IsType<CelError>(Evaluate("scores.filter(s, 1 / s == 1)", bindings));
        Assert.IsType<CelError>(Evaluate("scores.map(s, 1 / s)", bindings));
        Assert.IsType<CelError>(Evaluate("scores.transformList(i, v, 1 / v == 1, v)", bindings));
    }

    [Fact]
    public void Evaluate_ExistsWithoutDecisiveTrue_PropagatesPredicateError() {
        var bindings = new Dictionary<string, object?> { ["scores"] = new List<object?> { 2L, 0L } };

        Assert.IsType<CelError>(Evaluate("scores.exists(s, 1 / s == 1)", bindings));
    }

    [Fact]
    public void Evaluate_MacroTransforms_KeepShape() {
        var bindings = new Dictionary<string, object?> { ["scores"] = new List<object?> { 1L, 2L, 3L } };

        Assert.Equal(new List<object?> { 2L, 3L }, Evaluate("scores.filter(s, s > 1)", bindings));
        Assert.Equal(new List<object?> { 2L, 4L, 6L }, Evaluate("scores.map(s, s * 2)", bindings));
        Assert.Equal(new List<object?> { 2L, 3L }, Evaluate("scores.transformList(i, v, i > 0, v)", bindings));
    }
}

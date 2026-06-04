using System;
using System.Linq.Expressions;
using Xunit;

namespace Schemata.Expressions.Skeleton.Tests;

public class ExpressionRuntimeShould
{
    [Fact]
    public void Evaluate_UsesCompiledExpression() {
        Expression<Func<int, bool>> expression = value => value > 10;

        Assert.True(ExpressionRuntime.Evaluate(expression, 11));
        Assert.False(ExpressionRuntime.Evaluate(expression, 10));
    }

    [Fact]
    public void Evaluate_DoesNotReuseDelegateForDifferentClosureInstances() {
        var first  = GreaterThan(10);
        var second = GreaterThan(20);

        Assert.True(ExpressionRuntime.Evaluate(first, 15));
        Assert.False(ExpressionRuntime.Evaluate(second, 15));
    }

    [Fact]
    public void Evaluate_RejectsNullExpression() {
        Assert.Throws<ArgumentNullException>(() => ExpressionRuntime.Evaluate<int, bool>(null!, 1));
    }

    private static Expression<Func<int, bool>> GreaterThan(int threshold) { return value => value > threshold; }
}

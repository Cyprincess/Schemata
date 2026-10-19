using System;
using System.Linq.Expressions;
using Schemata.Common;
using Xunit;

namespace Schemata.Core.Tests.Common;

public class EvaluatorShould
{
    [Fact]
    public void PartialEval_EvaluateClosureToConstant() {
        var                         threshold = 10;
        Expression<Func<int, bool>> expr      = x => x > threshold;

        var result = Evaluator.PartialEval(expr);

        Assert.NotNull(result);

        // The closure reference (threshold) should be replaced with a constant.
        // Verify by compiling the result and checking it still works.
        var lambda   = (LambdaExpression)result;
        var compiled = ((Expression<Func<int, bool>>)lambda).Compile();

        Assert.True(compiled(15));
        Assert.False(compiled(5));

        // Verify the closure was actually evaluated: the body should not contain
        // a MemberAccess to the closure's field. Walk the expression tree.
        var body = lambda.Body;
        Assert.DoesNotContain("threshold", body.ToString());
    }

    [Fact]
    public void PartialEval_LeaveParameterUntouched() {
        Expression<Func<int, int>> expr = x => x + 1;

        var result = Evaluator.PartialEval(expr);

        Assert.NotNull(result);

        var lambda   = (LambdaExpression)result;
        var compiled = ((Expression<Func<int, int>>)lambda).Compile();

        Assert.Equal(6, compiled(5));
        Assert.Equal(1, compiled(0));

        // The parameter 'x' should still be present in the expression
        Assert.Single(lambda.Parameters);
        Assert.Equal("x", lambda.Parameters[0].Name);
    }

    [Fact]
    public void PartialEval_WithPrimitiveArithmetic_FoldsToConstant() {
        Expression<Func<int>> expr = () => 3 + 4 * 2;

        var result = Evaluator.PartialEval(expr);

        var constant = Assert.IsAssignableFrom<ConstantExpression>(((LambdaExpression)result!).Body);
        Assert.Equal(11, constant.Value);
    }

    [Fact]
    public void PartialEval_WithClosedMethodCall_LeavesCallUnfolded() {
        Expression<Func<int, bool>> expr = x => x > StaticSeed();

        var result = Evaluator.PartialEval(expr);

        var lambda = (LambdaExpression)result!;
        Assert.IsAssignableFrom<MethodCallExpression>(((BinaryExpression)lambda.Body).Right);
    }

    [Fact]
    public void PartialEval_WithUserDefinedOperator_LeavesBinaryUnfolded() {
        var left  = Expression.Constant(default(Money));
        var right = Expression.Constant(new Money());
        var body  = Expression.Add(left, right, typeof(Money).GetMethod("op_Addition")!);

        var result = Evaluator.PartialEval(body);

        Assert.IsAssignableFrom<BinaryExpression>(result);
    }

    [Fact]
    public void PartialEval_WithAggressivePredicate_DoesNotCompileByRefLikeNode() {
        var result = Evaluator.PartialEval(new ByRefLikeExtension(), _ => true);

        Assert.NotNull(result);
        Assert.IsType<ByRefLikeExtension>(result);
    }

    [Fact]
    public void PartialEval_WithOpaqueExtension_DefaultPredicate_ReturnsTreeUnchanged() {
        var result = Evaluator.PartialEval(new OpaqueExtension());

        Assert.NotNull(result);
        Assert.IsType<OpaqueExtension>(result);
    }

    [Fact]
    public void PartialEval_WithDecidedAndAlso_DoesNotEvaluateUnreachableBranch() {
        var box    = new Box { Value = 0 };
        var value  = Expression.Field(Expression.Constant(box), nameof(Box.Value));
        var divide = Expression.Divide(Expression.Constant(1), value);
        var x      = Expression.Parameter(typeof(int), "x");
        var body   = Expression.AndAlso(Expression.Constant(false), Expression.GreaterThan(divide, x));

        var result = (LambdaExpression)Evaluator.PartialEval(Expression.Lambda<Func<int, bool>>(body, x))!;

        Assert.IsType<ConstantExpression>(result.Body);
        Assert.Equal(false, ((ConstantExpression)result.Body).Value);
    }

    [Fact]
    public void PartialEval_WithDecidedOrElse_DoesNotEvaluateUnreachableBranch() {
        var box    = new Box { Value = 0 };
        var value  = Expression.Field(Expression.Constant(box), nameof(Box.Value));
        var divide = Expression.Divide(Expression.Constant(1), value);
        var body   = Expression.OrElse(Expression.Constant(true), Expression.LessThan(divide, Expression.Constant(0)));

        var result = Evaluator.PartialEval(body);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result!).Value);
    }

    [Fact]
    public void PartialEval_WithDecidedCondition_DoesNotEvaluateUnreachableBranch() {
        var box    = new Box { Value = 0 };
        var value  = Expression.Field(Expression.Constant(box), nameof(Box.Value));
        var divide = Expression.Divide(Expression.Constant(1), value);
        var body   = Expression.Condition(Expression.Constant(true), Expression.Constant(7), divide);

        var result = Evaluator.PartialEval(body);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(7, ((ConstantExpression)result!).Value);
    }

    [Fact]
    public void PartialEval_WithOpenShortCircuit_KeepsBothBranches() {
        Expression<Func<int, bool>> expr = x => x > 1 && x < 10;

        var result = (LambdaExpression)Evaluator.PartialEval(expr)!;

        var compiled = ((Expression<Func<int, bool>>)result).Compile();
        Assert.True(compiled(5));
        Assert.False(compiled(0));
        Assert.False(compiled(15));
    }

    [Fact]
    public void IsKeyExtractable_WithStaticFieldRead_ReturnsFalse() {
        var field = typeof(EvaluatorShould).GetField(nameof(StaticValue),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var expression = Expression.Field(null, field);

        Assert.False(Evaluator.IsKeyExtractable(expression));
    }

    [Fact]
    public void IsKeyExtractable_WithClosureInstanceField_ReturnsTrue() {
        var expression = Expression.Field(Expression.Constant(new Box()), nameof(Box.Value));

        Assert.True(Evaluator.IsKeyExtractable(expression));
    }

    private readonly struct Money
    {
        public static Money operator +(Money left, Money right) {
            return default;
        }
    }


    private static readonly int StaticValue = 42;
    private static int StaticSeed() {
        return 7;
    }

    private sealed class Box
    {
        public int Value = 3;
    }

    private sealed class ByRefLikeExtension : Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => typeof(ReadOnlySpan<char>);
    }

    private sealed class OpaqueExtension : Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => typeof(object);
    }
}

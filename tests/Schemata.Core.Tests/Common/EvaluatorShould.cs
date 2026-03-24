using System;
using System.Linq;
using System.Linq.Expressions;
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
}

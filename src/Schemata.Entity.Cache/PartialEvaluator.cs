using System.Collections.Generic;
using System.Linq.Expressions;

namespace Schemata.Entity.Cache;

/// <summary>
///     Replaces every closed sub-expression (one that does not depend on a
///     <see cref="ParameterExpression" />) with a <see cref="ConstantExpression" />
///     carrying the runtime value.
/// </summary>
/// <remarks>
///     <para>
///         Used by <see cref="Stringizing" /> to stabilize cache keys for queries that
///         capture local variables. Partial evaluation turns
///         <c>var min = 18; q.Where(s =&gt; s.Age &gt; min)</c> into an expression containing
///         <c>ConstantExpression(18)</c>, producing distinct strings per captured value.
///     </para>
///     <para>
///         Implementation follows the classic two-pass algorithm: a nominator marks
///         every subtree that contains no <see cref="ParameterExpression" />, then an
///         evaluator compiles + invokes each maximal nominated subtree and substitutes
///         the result.
///     </para>
/// </remarks>
public static class PartialEvaluator
{
    /// <summary>
    ///     Returns <paramref name="expression" /> with every closed sub-expression
    ///     evaluated and replaced by a <see cref="ConstantExpression" />.
    /// </summary>
    public static Expression Eval(Expression expression) {
        var nominator = new Nominator();
        nominator.Visit(expression);

        return new Evaluator(nominator.Candidates).Visit(expression) ?? expression;
    }

    #region Nested type: Evaluator

    private sealed class Evaluator : ExpressionVisitor
    {
        private readonly HashSet<Expression> _candidates;

        public Evaluator(HashSet<Expression> candidates) { _candidates = candidates; }

        public override Expression? Visit(Expression? node) {
            if (node is null) {
                return null;
            }

            return _candidates.Contains(node) ? Evaluate(node) : base.Visit(node);
        }

        private static Expression Evaluate(Expression node) {
            if (node.NodeType == ExpressionType.Constant) {
                return node;
            }

            var lambda = Expression.Lambda(node);
            var func   = lambda.Compile();
            var value  = func.DynamicInvoke();
            return Expression.Constant(value, node.Type);
        }
    }

    #endregion

    #region Nested type: Nominator

    private sealed class Nominator : ExpressionVisitor
    {
        private bool _cannotEvaluate;

        public HashSet<Expression> Candidates { get; } = new();

        public override Expression? Visit(Expression? node) {
            if (node is null) {
                return null;
            }

            var saved = _cannotEvaluate;
            _cannotEvaluate = false;
            base.Visit(node);

            if (!_cannotEvaluate) {
                if (CanEvaluateLocally(node)) {
                    Candidates.Add(node);
                } else {
                    _cannotEvaluate = true;
                }
            }

            _cannotEvaluate |= saved;
            return node;
        }

        private static bool CanEvaluateLocally(Expression node) {
            return node.NodeType switch {
                ExpressionType.Parameter      => false,
                ExpressionType.Lambda         => false,
                ExpressionType.Quote          => false,
                ExpressionType.New            => false,
                ExpressionType.NewArrayInit   => false,
                ExpressionType.NewArrayBounds => false,
                ExpressionType.MemberInit     => false,
                ExpressionType.ListInit       => false,
                var _                         => true,
            };
        }
    }

    #endregion
}

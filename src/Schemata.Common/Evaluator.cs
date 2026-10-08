using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Schemata.Common;

/// <summary>
///     Partially evaluates expression trees by collapsing subtrees that do not depend on
///     parameters into constants.
/// </summary>
public static class Evaluator
{
    /// <summary>
    ///     Partially evaluates the expression, collapsing sub-expressions that satisfy the
    ///     predicate into constants.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="fnCanBeEvaluated">A predicate that determines whether a sub-expression can be locally evaluated.</param>
    /// <returns>The simplified expression.</returns>
    public static Expression? PartialEval(Expression expression, Func<Expression, bool> fnCanBeEvaluated) {
        return new SubtreeEvaluator(new Nominator(fnCanBeEvaluated).Nominate(expression)).Eval(expression);
    }

    /// <summary>
    ///     Partially evaluates the expression, collapsing literals, closure field reads, and
    ///     primitive arithmetic over them into constants.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The simplified expression.</returns>
    public static Expression? PartialEval(Expression expression) {
        return PartialEval(expression, IsKeyExtractable);
    }

    /// <summary>
    ///     Indicates whether the node is a literal, a closure instance-field read, or primitive
    ///     arithmetic over those — the extractable set for cache-key evaluation. This is not a
    ///     general purity analysis: constructors, user-defined operators and conversions,
    ///     static field reads (which can trigger type initializers), and any call, indexer,
    ///     invocation, dynamic, or extension node are outside the set.
    /// </summary>
    /// <param name="expression">The node to inspect.</param>
    /// <returns><see langword="true" /> when the node can be evaluated to a constant for a cache key.</returns>
    public static bool IsKeyExtractable(Expression expression) {
        switch (expression.NodeType) {
            case ExpressionType.Constant:
                return true;
            case ExpressionType.Parameter:
            case ExpressionType.Call:
            case ExpressionType.Index:
            case ExpressionType.Invoke:
            case ExpressionType.Dynamic:
            case ExpressionType.Extension:
                return false;
            case ExpressionType.MemberAccess:
                return expression is MemberExpression { Expression: not null, Member: FieldInfo } field
                    && IsKeyExtractable(field.Expression);
            case ExpressionType.Conditional:
                var conditional = (ConditionalExpression)expression;
                return IsKeyExtractable(conditional.Test)
                    && IsKeyExtractable(conditional.IfTrue)
                    && IsKeyExtractable(conditional.IfFalse);
            default:
                if (expression is BinaryExpression binary) {
                    return binary.Method is null
                        && IsPureBinary(binary.NodeType)
                        && IsKeyExtractable(binary.Left)
                        && IsKeyExtractable(binary.Right);
                }

                if (expression is UnaryExpression unary) {
                    return unary.Method is null
                        && IsPureUnary(unary.NodeType)
                        && IsKeyExtractable(unary.Operand);
                }

                return false;
        }
    }

    // Arithmetic and comparison only: assignment, compound assignment, and increment nodes
    // mutate their target when compiled, so folding them would write closure state at
    // key-computation time.
    private static bool IsPureBinary(ExpressionType nodeType) {
        switch (nodeType) {
            case ExpressionType.Add:
            case ExpressionType.Subtract:
            case ExpressionType.Multiply:
            case ExpressionType.Divide:
            case ExpressionType.Modulo:
            case ExpressionType.And:
            case ExpressionType.Or:
            case ExpressionType.ExclusiveOr:
            case ExpressionType.LeftShift:
            case ExpressionType.RightShift:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.AndAlso:
            case ExpressionType.OrElse:
            case ExpressionType.Coalesce:
            case ExpressionType.ArrayIndex:
                return true;
            default:
                return false;
        }
    }

    private static bool IsPureUnary(ExpressionType nodeType) {
        switch (nodeType) {
            case ExpressionType.Not:
            case ExpressionType.Negate:
            case ExpressionType.NegateChecked:
            case ExpressionType.UnaryPlus:
            case ExpressionType.OnesComplement:
            case ExpressionType.Convert:
            case ExpressionType.ConvertChecked:
            case ExpressionType.TypeAs:
            case ExpressionType.ArrayLength:
                return true;
            default:
                return false;
        }
    }

    #region Nested type: Nominator

    private class Nominator : ExpressionVisitor
    {
        private readonly Func<Expression, bool> _fnCanBeEvaluated;
        private          HashSet<Expression>?   _candidates;
        private          bool                   _cannotBeEvaluated;

        internal Nominator(Func<Expression, bool> fnCanBeEvaluated) { _fnCanBeEvaluated = fnCanBeEvaluated; }

        internal HashSet<Expression> Nominate(Expression expression) {
            _candidates = [];

            Visit(expression);

            return _candidates;
        }

        public override Expression? Visit(Expression? expression) {
            if (expression is null) {
                return expression;
            }

            var saveCannotBeEvaluated = _cannotBeEvaluated;

            _cannotBeEvaluated = false;

            base.Visit(expression);

            // ByRef-like values (Span<T> and friends) cannot be boxed into a compiled closure,
            // so a compile attempt would throw at key-computation time; exclude the node and,
            // through _cannotBeEvaluated, every ancestor that contains it.
            if (expression.Type.IsByRefLike) {
                _cannotBeEvaluated = true;
            } else if (!_cannotBeEvaluated) {
                if (_fnCanBeEvaluated(expression)) {
                    _candidates?.Add(expression);
                } else {
                    _cannotBeEvaluated = true;
                }
            }

            _cannotBeEvaluated |= saveCannotBeEvaluated;

            return expression;
        }

        // Extension nodes cannot be compiled into a closure without reduction; exclude the node
        // without visiting its children, and through _cannotBeEvaluated every ancestor too, so
        // even an aggressive caller predicate cannot nominate one.
        protected override Expression VisitExtension(Expression node) {
            _cannotBeEvaluated = true;

            return node;
        }
    }

    #endregion

    #region Nested type: SubtreeEvaluator

    private class SubtreeEvaluator : ExpressionVisitor
    {
        private readonly HashSet<Expression> _candidates;

        internal SubtreeEvaluator(HashSet<Expression> candidates) { _candidates = candidates; }

        internal Expression? Eval(Expression? exp) { return Visit(exp); }

        public override Expression? Visit(Expression? exp) {
            if (exp is null) {
                return null;
            }

            if (_candidates.Contains(exp)) {
                return Evaluate(exp);
            }

            return base.Visit(exp);
        }

        // Extension reduction belongs to the owning provider; keep the subtree untouched even
        // if a candidate set from a custom predicate were to mention it.
        protected override Expression VisitExtension(Expression node) {
            return node;
        }

        private static Expression Evaluate(Expression e) {
            if (e.NodeType == ExpressionType.Constant) {
                return e;
            }

            var lambda   = Expression.Lambda(e);
            var fn       = lambda.Compile();
            var constant = fn.DynamicInvoke(null);

            var type = e.Type;
            if (constant is not null && type.IsArray && type.GetElementType() == constant.GetType().GetElementType()) {
                type = constant.GetType();
            }

            return Expression.Constant(constant, type);
        }
    }

    #endregion
}

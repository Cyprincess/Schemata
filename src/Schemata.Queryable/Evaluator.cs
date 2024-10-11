using System.Collections.Generic;
using System.Linq.Expressions;

// ReSharper disable once CheckNamespace
namespace System.Linq;

public static class Evaluator
{
    public static Expression? PartialEval(Expression expression, Func<Expression, bool> fnCanBeEvaluated) {
        return new SubtreeEvaluator(new Nominator(fnCanBeEvaluated).Nominate(expression)).Eval(expression);
    }

    public static Expression? PartialEval(Expression expression) {
        return PartialEval(expression, CanBeEvaluatedLocally);
    }

    private static bool CanBeEvaluatedLocally(Expression expression) {
        return expression.NodeType != ExpressionType.Parameter;
    }

    #region Nested type: Nominator

    private class Nominator : ExpressionVisitor
    {
        private readonly Func<Expression, bool> _fnCanBeEvaluated;
        private          HashSet<Expression>?   _candidates;
        private          bool                   _cannotBeEvaluated;

        internal Nominator(Func<Expression, bool> fnCanBeEvaluated) {
            _fnCanBeEvaluated = fnCanBeEvaluated;
        }

        internal HashSet<Expression> Nominate(Expression expression) {
            _candidates = [];

            Visit(expression);

            return _candidates;
        }

        public override Expression? Visit(Expression? expression) {
            if (expression == null) {
                return expression;
            }

            var saveCannotBeEvaluated = _cannotBeEvaluated;

            _cannotBeEvaluated = false;

            base.Visit(expression);

            if (!_cannotBeEvaluated) {
                if (_fnCanBeEvaluated(expression)) {
                    _candidates?.Add(expression);
                } else {
                    _cannotBeEvaluated = true;
                }
            }

            _cannotBeEvaluated |= saveCannotBeEvaluated;

            return expression;
        }
    }

    #endregion

    #region Nested type: SubtreeEvaluator

    private class SubtreeEvaluator : ExpressionVisitor
    {
        private readonly HashSet<Expression> _candidates;

        internal SubtreeEvaluator(HashSet<Expression> candidates) {
            _candidates = candidates;
        }

        internal Expression? Eval(Expression? exp) {
            return Visit(exp);
        }

        public override Expression? Visit(Expression? exp) {
            if (exp == null) {
                return null;
            }

            if (_candidates.Contains(exp)) {
                return Evaluate(exp);
            }

            return base.Visit(exp);
        }

        private static Expression Evaluate(Expression e) {
            if (e.NodeType == ExpressionType.Constant) {
                return e;
            }

            var lambda   = Expression.Lambda(e);
            var fn       = lambda.Compile();
            var constant = fn.DynamicInvoke(null);

            var type = e.Type;
            if (constant != null && type.IsArray && type.GetElementType() == constant.GetType().GetElementType()) {
                type = constant.GetType();
            }

            return Expression.Constant(constant, type);
        }
    }

    #endregion
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Schemata.Entity.Cache;

public static class LambdaCompare
{
    public static bool Eq<TSource, TValue>(Expression<Func<TSource, TValue>> x, Expression<Func<TSource, TValue>> y) {
        return ExpressionsEqual(x, y, null, null);
    }

    public static bool Eq<TSource1, TSource2, TValue>(
        Expression<Func<TSource1, TSource2, TValue>> x,
        Expression<Func<TSource1, TSource2, TValue>> y) {
        return ExpressionsEqual(x, y, null, null);
    }

    public static Expression<Func<Expression<Func<TSource, TValue>>, bool>> Eq<TSource, TValue>(
        Expression<Func<TSource, TValue>> y) {
        return x => ExpressionsEqual(x, y, null, null);
    }

    private static bool ExpressionsEqual(
        Expression?       x,
        Expression?       y,
        LambdaExpression? rootX,
        LambdaExpression? rootY) {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        var valueX = TryCalculateConstant(x);
        var valueY = TryCalculateConstant(y);

        if (valueX.IsDefined && valueY.IsDefined) {
            return ValuesEqual(valueX.Value, valueY.Value);
        }

        if (x.NodeType != y.NodeType || x.Type != y.Type) {
            if (IsAnonymousType(x.Type) && IsAnonymousType(y.Type)) {
                throw new NotImplementedException("Comparison of Anonymous Types is not supported");
            }

            return false;
        }

        switch (x) {
            case LambdaExpression lx:
            {
                var ly      = (LambdaExpression)y;
                var paramsX = lx.Parameters;
                var paramsY = ly.Parameters;
                return CollectionsEqual(paramsX, paramsY, lx, ly) && ExpressionsEqual(lx.Body, ly.Body, lx, ly);
            }
            case MemberExpression mex:
            {
                var mey = (MemberExpression)y;
                return Equals(mex.Member, mey.Member) && ExpressionsEqual(mex.Expression, mey.Expression, rootX, rootY);
            }
            case BinaryExpression bx:
            {
                var by = (BinaryExpression)y;
                return bx.Method == by.Method
                    && ExpressionsEqual(bx.Left, by.Left, rootX, rootY)
                    && ExpressionsEqual(bx.Right, by.Right, rootX, rootY);
            }
            case UnaryExpression ux:
            {
                var uy = (UnaryExpression)y;
                return ux.Method == uy.Method && ExpressionsEqual(ux.Operand, uy.Operand, rootX, rootY);
            }
            case ParameterExpression px:
            {
                var py = (ParameterExpression)y;
                return rootX?.Parameters.IndexOf(px) == rootY?.Parameters.IndexOf(py);
            }
            case MethodCallExpression expression:
            {
                var cy = (MethodCallExpression)y;
                return expression.Method == cy.Method
                    && ExpressionsEqual(expression.Object, cy.Object, rootX, rootY)
                    && CollectionsEqual(expression.Arguments, cy.Arguments, rootX, rootY);
            }
            case MemberInitExpression mix:
            {
                var miy = (MemberInitExpression)y;
                return ExpressionsEqual(mix.NewExpression, miy.NewExpression, rootX, rootY)
                    && MemberInitsEqual(mix.Bindings, miy.Bindings, rootX, rootY);
            }
            case NewArrayExpression arrayExpression:
            {
                var ny = (NewArrayExpression)y;
                return CollectionsEqual(arrayExpression.Expressions, ny.Expressions, rootX, rootY);
            }
            case NewExpression nx:
            {
                var ny = (NewExpression)y;
                return Equals(nx.Constructor, ny.Constructor)
                    && CollectionsEqual(nx.Arguments, ny.Arguments, rootX, rootY)
                    && ((nx.Members == null && ny.Members == null)
                     || (nx.Members != null && ny.Members != null && CollectionsEqual(nx.Members, ny.Members)));
            }
            case ConditionalExpression cx:
            {
                var cy = (ConditionalExpression)y;
                return ExpressionsEqual(cx.Test, cy.Test, rootX, rootY)
                    && ExpressionsEqual(cx.IfFalse, cy.IfFalse, rootX, rootY)
                    && ExpressionsEqual(cx.IfTrue, cy.IfTrue, rootX, rootY);
            }
            default:
                throw new NotImplementedException(x.ToString());
        }
    }

    private static bool IsAnonymousType(Type type) {
        var hasCompilerGeneratedAttribute
            = type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Length != 0;
        var nameContainsAnonymousType = type.FullName?.Contains("AnonymousType");
        var isAnonymousType           = hasCompilerGeneratedAttribute && nameContainsAnonymousType == true;

        return isAnonymousType;
    }

    private static bool MemberInitsEqual(
        ICollection<MemberBinding> bx,
        ICollection<MemberBinding> by,
        LambdaExpression?          rootX,
        LambdaExpression?          rootY) {
        if (bx.Count != by.Count) {
            return false;
        }

        if (bx.Concat(by).Any(b => b.BindingType != MemberBindingType.Assignment)) {
            throw new NotImplementedException("Only MemberBindingType.Assignment is supported");
        }

        return bx.Cast<MemberAssignment>()
                 .OrderBy(b => b.Member.Name)
                 .Select((b, i) => new {
                      Expr = b.Expression,
                      b.Member,
                      Index = i,
                  })
                 .Join(by.Cast<MemberAssignment>()
                     .OrderBy(b => b.Member.Name)
                     .Select((b, i) => new {
                          Expr = b.Expression,
                          b.Member,
                          Index = i,
                      }),
                      o => o.Index,
                      o => o.Index,
                      (xe, ye) => new {
                          XExpr   = xe.Expr,
                          XMember = xe.Member,
                          YExpr   = ye.Expr,
                          YMember = ye.Member,
                      })
                 .All(o => Equals(o.XMember, o.YMember) && ExpressionsEqual(o.XExpr, o.YExpr, rootX, rootY));
    }

    private static bool ValuesEqual(object? x, object? y) {
        if (ReferenceEquals(x, y)) {
            return true;
        }

        if (x is ICollection collection && y is ICollection collection1) {
            return CollectionsEqual(collection, collection1);
        }

        return Equals(x, y);
    }

    private static ConstantValue TryCalculateConstant(Expression? e) {
        if (e is ConstantExpression expression) {
            return new(true, expression.Value);
        }

        if (e is MemberExpression me) {
            var parentValue = TryCalculateConstant(me.Expression);
            if (parentValue.IsDefined) {
                var result = me.Member is FieldInfo info
                    ? info.GetValue(parentValue.Value)
                    : ((PropertyInfo)me.Member).GetValue(parentValue.Value);
                return new(true, result);
            }
        }

        if (e is NewArrayExpression ae) {
            var result = ae.Expressions.Select(TryCalculateConstant);
            if (result.All(i => i.IsDefined)) {
                return new(true, result.Select(i => i.Value).ToArray());
            }
        }

        if (e is ConditionalExpression ce) {
            var evaluatedTest = TryCalculateConstant(ce.Test);
            if (evaluatedTest.IsDefined) {
                return TryCalculateConstant(Equals(evaluatedTest.Value, true) ? ce.IfTrue : ce.IfFalse);
            }
        }

        return default;
    }

    private static bool CollectionsEqual(
        IEnumerable<Expression>? x,
        IEnumerable<Expression>? y,
        LambdaExpression?        rootX,
        LambdaExpression?        rootY) {
        if (x == null && y == null) {
            return true;
        }

        if (x == null || y == null) {
            return false;
        }

        return x.Count() == y.Count()
     && x.Select((e, i) => new {
              Expr  = e,
              Index = i,
          })
         .Join(y.Select((e, i) => new {
                  Expr  = e,
                  Index = i,
              }),
              o => o.Index,
              o => o.Index,
              (xe, ye) => new {
                  X = xe.Expr,
                  Y = ye.Expr,
              })
         .All(o => ExpressionsEqual(o.X, o.Y, rootX, rootY));
    }

    private static bool CollectionsEqual(ICollection x, ICollection y) {
        return x.Count == y.Count
     && x.Cast<object>()
         .Select((e, i) => new {
              Expr  = e,
              Index = i,
          })
         .Join(y.Cast<object>()
             .Select((e, i) => new {
                  Expr  = e,
                  Index = i,
              }),
              o => o.Index,
              o => o.Index,
              (xe, ye) => new {
                  X = xe.Expr,
                  Y = ye.Expr,
              })
         .All(o => Equals(o.X, o.Y));
    }

    #region Nested type: ConstantValue

    private struct ConstantValue(bool isDefined, object? value)
    {
        public bool IsDefined { get; } = isDefined;

        public object? Value { get; } = value;
    }

    #endregion
}

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Compiles AIP-132 order-by expressions into query ordering functions.
/// </summary>
public sealed class AipOrderCompiler : IOrderCompiler
{
    #region IOrderCompiler Members

    public string Language => AipLanguage.Name;

    public Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string                    source,
        ExpressionCompileOptions? options = null
    ) {
        var order = AipParser.Order.Parse(source)
                 ?? throw new ArgumentException("Invalid AIP order expression.", nameof(source));
        return query => {
            IOrderedQueryable<T>? ordered = null;

            foreach (var item in order) {
                var visitor = new AipCompileVisitor(typeof(T), options);
                var body    = visitor.Visit(item.Key);
                var lambda  = Expression.Lambda(body, visitor.Parameter);
                ordered = Apply(query, ordered, lambda, item.Value);
            }

            return ordered ?? query.OrderBy(_ => 0);
        };
    }

    #endregion

    private static IOrderedQueryable<T> Apply<T>(
        IQueryable<T>         source,
        IOrderedQueryable<T>? ordered,
        LambdaExpression      key,
        Ordering              direction
    ) {
        var methodName = ordered is null ? direction == Ordering.Ascending
                ? nameof(Queryable.OrderBy)
                : nameof(Queryable.OrderByDescending) :
            direction == Ordering.Ascending ? nameof(Queryable.ThenBy) : nameof(Queryable.ThenByDescending);

        var method = typeof(Queryable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                      .Single(m => m.Name == methodName && m.GetParameters().Length == 2)
                                      .MakeGenericMethod(typeof(T), key.Body.Type);

        var target = ordered ?? source;
        return (IOrderedQueryable<T>)method.Invoke(null, [target, key])!;
    }
}

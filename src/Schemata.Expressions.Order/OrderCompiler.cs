using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Schemata.Common;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Order;

/// <summary>
///     Compiles AIP-132 order-by expressions into query ordering functions, independent of the
///     filter expression language.
/// </summary>
public sealed class OrderCompiler : IOrderCompiler
{
    #region IOrderCompiler Members

    public Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string                    source,
        ExpressionCompileOptions? options = null
    ) {
        var keys    = Parse(source);
        var lambdas = new List<(LambdaExpression Key, bool Descending)>(keys.Count);

        foreach (var key in keys) {
            var        parameter = Expression.Parameter(typeof(T), "entity");
            Expression? body      = parameter;
            foreach (var segment in key.Path) {
                body = MemberAccess.Resolve(body, segment);
                if (body is null) {
                    throw new ArgumentException($"Unknown order field '{string.Join(".", key.Path)}'.", nameof(source));
                }
            }

            lambdas.Add((Expression.Lambda(body, parameter), key.Descending));
        }

        return query => {
            IOrderedQueryable<T>? ordered = null;
            foreach (var (key, descending) in lambdas) {
                ordered = Apply(query, ordered, key, descending);
            }

            return ordered ?? query.OrderBy(_ => 0);
        };
    }

    public IReadOnlyList<OrderKey> Parse(string source) {
        var keys = new List<OrderKey>();

        foreach (var segment in source.Split(',')) {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 2) {
                throw new ArgumentException($"Invalid order segment '{trimmed}'.", nameof(source));
            }

            var descending = tokens.Length == 2
                ? tokens[1].ToLowerInvariant() switch {
                    "desc" => true,
                    "asc"  => false,
                    var _  => throw new ArgumentException($"Invalid order direction '{tokens[1]}'.", nameof(source)),
                }
                : false;

            keys.Add(new(tokens[0].Split('.'), descending));
        }

        return keys;
    }

    #endregion

    private static IOrderedQueryable<T> Apply<T>(
        IQueryable<T>         source,
        IOrderedQueryable<T>? ordered,
        LambdaExpression      key,
        bool                  descending
    ) {
        var methodName = ordered is null
            ? descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy)
            : descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);

        var method = typeof(Queryable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                      .Single(m => m.Name == methodName && m.GetParameters().Length == 2)
                                      .MakeGenericMethod(typeof(T), key.Body.Type);

        var target = ordered ?? source;
        return (IOrderedQueryable<T>)method.Invoke(null, [target, key])!;
    }
}

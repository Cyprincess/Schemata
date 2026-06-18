using System;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

public sealed class CelCompiler : IExpressionCompiler
{
    #region IExpressionCompiler Members

    public string Language => CelLanguage.Name;

    public IExpressionTree Parse(string source) {
        if (source is null) {
            throw new ArgumentNullException(nameof(source));
        }

        var key = ExpressionCacheKey.Create(Language, source, null, null, null);
        return ExpressionCache.GetOrAddTree(key, () => {
            var node = CelParser.Expression.Parse(source)
                    ?? throw new ArgumentException("Invalid CEL expression.", nameof(source));
            node.Source = source;
            return node;
        });
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    ) {
        if (tree is not CelNode node) {
            throw new ArgumentException("Tree must be a CEL node.", nameof(tree));
        }

        var key = ExpressionCacheKey.Create(Language, node.Source, typeof(TContext), typeof(TResult), Fingerprint(options));
        return ExpressionCache.GetOrAddExpression(key, () => {
            var visitor = new CelCompileVisitor(typeof(TContext), options);
            var body    = visitor.Visit(node);
            if (body.Type != typeof(TResult)) {
                body = Expression.Convert(body, typeof(TResult));
            }

            return Expression.Lambda<Func<TContext, TResult>>(body, visitor.Parameter);
        });
    }

    #endregion

    // Custom functions are the only options-dependent compile input; their identity must be part of the
    // cache key so two option sets that bind the same name to different delegates do not share a result.
    private static string Fingerprint(ExpressionCompileOptions? options) {
        if (options is null || options.Functions.Count == 0) {
            return "builtins:v1;functions:none";
        }

        return "builtins:v1;functions:" + string.Join(
            ",",
            options.Functions.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                   .Select(kv => $"{kv.Key}:{RuntimeHelpers.GetHashCode(kv.Value)}"));
    }
}

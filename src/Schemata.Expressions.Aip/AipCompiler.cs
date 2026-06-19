using System;
using System.Linq.Expressions;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Parses and compiles AIP-160 filter expressions.
/// </summary>
public sealed class AipCompiler : IExpressionCompiler
{
    #region IExpressionCompiler Members

    public string Language => AipLanguage.Name;

    public IExpressionTree Parse(string source) {
        if (source is null) {
            throw new ArgumentNullException(nameof(source));
        }

        var key = ExpressionCacheKey.Create(Language, source, null, null, null);
        return ExpressionCache.GetOrAddTree(key, () => {
            var filter = AipParser.Filter.Parse(source) ?? throw new ArgumentException("Invalid AIP filter.", nameof(source));
            filter.Source = source;
            return filter;
        });
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    ) {
        if (tree is not Filter filter) {
            throw new ArgumentException("Tree must be an AIP filter.", nameof(tree));
        }

        var key = ExpressionCacheKey.Create(Language, filter.Source, typeof(TContext), typeof(TResult), AipBuiltInFunctions.Fingerprint(options));
        return ExpressionCache.GetOrAddExpression(key, () => {
            var visitor = new AipCompileVisitor(typeof(TContext), options);
            var body    = visitor.Visit(filter);

            if (body.Type != typeof(TResult)) {
                body = Expression.Convert(body, typeof(TResult));
            }

            return Expression.Lambda<Func<TContext, TResult>>(body, visitor.Parameter);
        });
    }

    #endregion
}

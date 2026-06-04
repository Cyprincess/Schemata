using System;
using System.Linq.Expressions;
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
        return ExpressionCache.GetOrAddTree(key, () => CelParser.Expression.Parse(source)
                                                    ?? throw new ArgumentException(
                                                           "Invalid CEL expression.", nameof(source)));
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    ) {
        if (tree is not CelNode node) {
            throw new ArgumentException("Tree must be a CEL node.", nameof(tree));
        }

        var visitor = new CelCompileVisitor(typeof(TContext), options);
        var body    = visitor.Visit(node);
        if (body.Type != typeof(TResult)) {
            body = Expression.Convert(body, typeof(TResult));
        }

        return Expression.Lambda<Func<TContext, TResult>>(body, visitor.Parameter);
    }

    #endregion
}

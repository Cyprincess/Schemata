using System;
using System.Linq.Expressions;

namespace Schemata.Expressions.Skeleton;

public interface IExpressionCompiler
{
    string Language { get; }

    IExpressionTree Parse(string source);

    Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    );
}

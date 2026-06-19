using System;
using System.Linq.Expressions;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Parses and compiles expression-language source into typed LINQ expression trees.
/// </summary>
public interface IExpressionCompiler
{
    /// <summary>
    ///     Gets the language identifier handled by this compiler.
    /// </summary>
    string Language { get; }

    /// <summary>
    ///     Parses source text into an expression tree for this language.
    /// </summary>
    IExpressionTree Parse(string source);

    /// <summary>
    ///     Compiles a parsed expression tree into a typed LINQ expression.
    /// </summary>
    Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    );
}

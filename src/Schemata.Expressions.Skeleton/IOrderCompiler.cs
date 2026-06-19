using System;
using System.Linq;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Compiles order-by source text into query ordering functions.
/// </summary>
public interface IOrderCompiler
{
    /// <summary>
    ///     Gets the language identifier handled by this order compiler.
    /// </summary>
    string Language { get; }

    /// <summary>
    ///     Compiles source text into a function that applies ordering to a query.
    /// </summary>
    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(string source, ExpressionCompileOptions? options = null);
}

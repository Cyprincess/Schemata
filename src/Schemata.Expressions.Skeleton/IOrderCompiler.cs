using System;
using System.Collections.Generic;
using System.Linq;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Compiles AIP-132 order-by source text into query ordering functions.
/// </summary>
public interface IOrderCompiler
{
    /// <summary>
    ///     Compiles source text into a function that applies ordering to a query.
    /// </summary>
    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(string source, ExpressionCompileOptions? options = null);

    /// <summary>
    ///     Parses source text into ordered sort keys.
    /// </summary>
    IReadOnlyList<OrderKey> Parse(string source);
}

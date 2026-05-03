using System;
using System.Linq.Expressions;

namespace Schemata.Resource.Foundation.Grammars;

/// <summary>
///     Wraps a custom function factory for filter expressions
///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>. Registered
///     via <see cref="Container.RegisterFunction" /> so the parser can resolve
///     function call tokens to LINQ expressions.
/// </summary>
public class FilterFunction
{
    /// <summary>
    ///     Initializes a new filter function with the specified factory.
    /// </summary>
    /// <param name="factory">A factory that produces an expression from argument expressions and the container.</param>
    public FilterFunction(Func<Expression[], Container, Expression> factory) { Factory = factory; }

    /// <summary>
    ///     Gets the factory delegate that produces a LINQ expression from argument expressions.
    /// </summary>
    public Func<Expression[], Container, Expression> Factory { get; }
}

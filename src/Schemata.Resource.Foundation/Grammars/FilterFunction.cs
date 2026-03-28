using System;
using System.Linq.Expressions;

namespace Schemata.Resource.Foundation.Grammars;

/// <summary>
///     Wraps a custom function factory for use in filter expressions.
/// </summary>
public class FilterFunction
{
    /// <summary>
    ///     Initializes a new filter function with the specified expression factory.
    /// </summary>
    /// <param name="factory">A factory that produces an expression from argument expressions and the container.</param>
    public FilterFunction(Func<Expression[], Container, Expression> factory) { Factory = factory; }

    /// <summary>
    ///     Gets the factory delegate that produces a LINQ expression from argument expressions.
    /// </summary>
    public Func<Expression[], Container, Expression> Factory { get; }
}

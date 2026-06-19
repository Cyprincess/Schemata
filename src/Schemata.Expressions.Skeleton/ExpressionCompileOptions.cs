using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Supplies optional bindings used while compiling expression trees.
/// </summary>
public sealed class ExpressionCompileOptions
{
    /// <summary>
    ///     Gets custom functions available to expression compilers by name.
    /// </summary>
    public IDictionary<string, ExpressionFunction> Functions { get; } = new Dictionary<string, ExpressionFunction>();
}

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Wraps a factory that converts argument expressions into a function call expression.
/// </summary>
public sealed class ExpressionFunction
{
    /// <summary>
    ///     Creates a function binding from an expression factory.
    /// </summary>
    public ExpressionFunction(Func<IReadOnlyList<Expression>, Expression> factory) {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    ///     Gets the expression factory for this function binding.
    /// </summary>
    public Func<IReadOnlyList<Expression>, Expression> Factory { get; }

    /// <summary>
    ///     Builds the expression represented by this function binding.
    /// </summary>
    public Expression Build(IReadOnlyList<Expression> args) { return Factory(args); }
}

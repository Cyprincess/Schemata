using System;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Signals that an expression is malformed or cannot be compiled, independent of the source
///     language. Modules catch this to surface a validation error without referencing a concrete
///     language package.
/// </summary>
public sealed class ExpressionException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The failure description.</param>
    public ExpressionException(string message) : base(message) { }

    /// <summary>Creates an exception wrapping a language-specific failure.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying language-specific exception.</param>
    public ExpressionException(string message, Exception innerException) : base(message, innerException) { }
}

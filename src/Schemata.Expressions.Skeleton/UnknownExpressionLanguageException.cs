using System;
using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Thrown when a requested expression language is not enabled for the module, or the module
///     enables no languages.
/// </summary>
public sealed class UnknownExpressionLanguageException : Exception
{
    /// <summary>
    ///     Creates an exception for a requested language that is not enabled.
    /// </summary>
    public UnknownExpressionLanguageException(string? requested, IReadOnlyList<string> available)
        : base($"Expression language '{requested}' is not enabled. Available: {string.Join(", ", available)}.") {
        Requested = requested;
        Available = available;
    }

    /// <summary>
    ///     Gets the requested language identifier.
    /// </summary>
    public string? Requested { get; }

    /// <summary>
    ///     Gets the languages enabled for the module.
    /// </summary>
    public IReadOnlyList<string> Available { get; }
}

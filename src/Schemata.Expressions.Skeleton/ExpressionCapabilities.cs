using System;
using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Declares which expression constructs a backend can translate to its native query, so a
///     pushdown planner can split a parsed expression into a pushable part and a local residual.
///     Constructs not covered here are treated as non-pushable and evaluated locally.
/// </summary>
public sealed class ExpressionCapabilities
{
    /// <summary>
    ///     A conservative set covering constructs every relational backend translates: comparison,
    ///     boolean composition, presence, wildcard text match, arithmetic, membership, and the
    ///     common string-match functions. Language-specific extras (regex, macros, custom functions)
    ///     are excluded and therefore run locally.
    /// </summary>
    public static ExpressionCapabilities Relational { get; } = new();

    /// <summary>
    ///     Gets whether comparison operators are translatable.
    /// </summary>
    public bool Comparison { get; init; } = true;

    /// <summary>
    ///     Gets whether boolean composition (and / or / not) is translatable.
    /// </summary>
    public bool Logical { get; init; } = true;

    /// <summary>
    ///     Gets whether field presence tests are translatable.
    /// </summary>
    public bool Presence { get; init; } = true;

    /// <summary>
    ///     Gets whether wildcard text matches are translatable to a LIKE form.
    /// </summary>
    public bool Wildcard { get; init; } = true;

    /// <summary>
    ///     Gets whether numeric arithmetic is translatable.
    /// </summary>
    public bool Arithmetic { get; init; } = true;

    /// <summary>
    ///     Gets whether membership tests against a collection are translatable.
    /// </summary>
    public bool Membership { get; init; } = true;

    /// <summary>
    ///     Gets whether substring match functions are translatable.
    /// </summary>
    public bool StringMatch { get; init; } = true;

    /// <summary>
    ///     Gets the additional named functions the backend can translate beyond the built-in flags.
    /// </summary>
    public IReadOnlyCollection<string> Functions { get; init; } = Array.Empty<string>();
}

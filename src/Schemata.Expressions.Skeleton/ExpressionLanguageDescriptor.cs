namespace Schemata.Expressions.Skeleton;

/// <summary>
///     The global defaults registered for an expression language, looked up by language name when
///     resolving a filter's effective execution settings.
/// </summary>
/// <param name="Language">The language identifier.</param>
/// <param name="Filtering">The language's default filtering mode.</param>
/// <param name="MaxResidualScanRows">The language's default residual scan cap; 0 inherits.</param>
/// <param name="SupportsValues">Whether the language compiles scalar value expressions (not just boolean predicates).</param>
public sealed record ExpressionLanguageDescriptor(
    string        Language,
    FilteringMode Filtering,
    int           MaxResidualScanRows,
    bool          SupportsValues = false);

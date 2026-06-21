namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Configures an expression language's global defaults at registration. These form the first
///     level combined with module and per-language settings when resolving a filter's effective mode.
/// </summary>
public sealed class ExpressionLanguageOptions
{
    /// <summary>
    ///     Gets or sets the language's default filtering mode.
    /// </summary>
    public FilteringMode Filtering { get; set; }

    /// <summary>
    ///     Gets or sets the language's default cap on rows scanned during residual evaluation;
    ///     0 leaves it to a higher level or the built-in default.
    /// </summary>
    public int MaxResidualScanRows { get; set; }
}

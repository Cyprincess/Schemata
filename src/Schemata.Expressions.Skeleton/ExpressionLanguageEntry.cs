namespace Schemata.Expressions.Skeleton;

/// <summary>
///     A language a module enables, with optional module-scoped overrides of the language's defaults.
/// </summary>
public sealed class ExpressionLanguageEntry
{
    /// <summary>
    ///     Creates an entry for the named language.
    /// </summary>
    public ExpressionLanguageEntry(string language) {
        Language = language;
    }

    /// <summary>
    ///     Gets the language identifier.
    /// </summary>
    public string Language { get; }

    /// <summary>
    ///     Gets or sets this module's override of the language's filtering mode; combined by
    ///     intersection with the other levels.
    /// </summary>
    public FilteringMode Filtering { get; set; }

    /// <summary>
    ///     Gets or sets this module's override of the residual scan cap for the language; 0 inherits.
    /// </summary>
    public int MaxResidualScanRows { get; set; }
}
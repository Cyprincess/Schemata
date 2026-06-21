using System;
using System.Collections.Generic;

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

/// <summary>
///     The expression languages a module enables, in priority order. The first enabled language is
///     the module default; a call site may request another enabled language explicitly.
/// </summary>
public sealed class ExpressionLanguageProfile
{
    /// <summary>
    ///     Gets the enabled languages in priority order.
    /// </summary>
    public IList<ExpressionLanguageEntry> Languages { get; } = new List<ExpressionLanguageEntry>();

    /// <summary>
    ///     Gets or sets the module-level filtering mode; combined by intersection with the language
    ///     and per-language levels.
    /// </summary>
    public FilteringMode Filtering { get; set; }

    /// <summary>
    ///     Gets or sets the module-level residual scan cap; 0 inherits.
    /// </summary>
    public int MaxResidualScanRows { get; set; }

    /// <summary>
    ///     Enables a language, returning the existing entry when already enabled so callers can
    ///     adjust its module-scoped overrides. New languages append in priority order.
    /// </summary>
    /// <param name="language">The language identifier to enable.</param>
    /// <returns>The new or existing entry for the language.</returns>
    public ExpressionLanguageEntry Enable(string language) {
        foreach (var entry in Languages) {
            if (string.Equals(entry.Language, language, StringComparison.Ordinal)) {
                return entry;
            }
        }

        var added = new ExpressionLanguageEntry(language);
        Languages.Add(added);
        return added;
    }
}

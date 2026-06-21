using System;
using System.Linq;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Resolves which enabled language handles a request and the effective filtering mode formed by
///     intersecting the language, module, and per-language levels.
/// </summary>
public static class ExpressionLanguageResolver
{
    /// <summary>
    ///     Resolves the language for an optional explicit request against a module's profile. An
    ///     explicit language must be enabled; otherwise the first enabled language is the default.
    /// </summary>
    /// <param name="profile">The module's enabled languages and module-level settings.</param>
    /// <param name="requested">The explicitly requested language, or null/empty for the default.</param>
    /// <param name="descriptors">Looks up a language's registered global defaults by name.</param>
    public static ResolvedLanguage Resolve(
        ExpressionLanguageProfile                   profile,
        string?                                     requested,
        Func<string, ExpressionLanguageDescriptor?> descriptors
    ) {
        var entries = profile.Languages;
        if (entries.Count == 0) {
            throw new UnknownExpressionLanguageException(requested, []);
        }

        ExpressionLanguageEntry entry;
        if (string.IsNullOrWhiteSpace(requested)) {
            entry = entries[0];
        } else {
            entry = entries.FirstOrDefault(e => string.Equals(e.Language, requested, StringComparison.Ordinal))
                 ?? throw new UnknownExpressionLanguageException(
                        requested, entries.Select(e => e.Language).ToArray());
        }

        var descriptor = descriptors(entry.Language);

        var mode = (descriptor?.Filtering ?? FilteringMode.Default)
                  .Narrow(profile.Filtering)
                  .Narrow(entry.Filtering)
                  .OrStrict();

        var max = Positive(entry.MaxResidualScanRows)
               ?? Positive(profile.MaxResidualScanRows)
               ?? Positive(descriptor?.MaxResidualScanRows ?? 0)
               ?? 10_000;

        return new(entry.Language, mode, max);
    }

    private static int? Positive(int value) {
        return value > 0 ? value : null;
    }
}

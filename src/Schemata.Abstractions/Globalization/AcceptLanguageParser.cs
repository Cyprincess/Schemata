using System;
using System.Collections.Generic;
using System.Globalization;

namespace Schemata.Abstractions.Globalization;

/// <summary>
///     Resolves the request culture from <c>Accept-Language</c> header values per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9110.html#name-accept-language">
///         RFC 9110 §12.5.4: Accept-Language
///     </seealso>.
/// </summary>
public static class AcceptLanguageParser
{
    /// <summary>
    ///     Returns the highest-quality resolvable culture, or <see langword="null" /> when
    ///     the header is absent, contains only wildcards, or names no known culture.
    /// </summary>
    /// <param name="values">Raw header values in arrival order.</param>
    public static CultureInfo? Parse(IEnumerable<string?>? values) {
        if (values is null) {
            return null;
        }

        CultureInfo? best  = null;
        var          bestQ = -1d;

        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            foreach (var segment in value.Split(',')) {
                var (tag, quality) = Split(segment);
                if (tag.Length == 0 || tag == "*" || quality <= 0d || quality <= bestQ) {
                    continue;
                }

                CultureInfo culture;
                try {
                    culture = CultureInfo.GetCultureInfo(tag);
                } catch (CultureNotFoundException) {
                    continue;
                }

                best  = culture;
                bestQ = quality;
            }
        }

        return best;
    }

    private static (string Tag, double Quality) Split(string segment) {
        var trimmed   = segment.Trim();
        var semicolon = trimmed.IndexOf(';');
        if (semicolon < 0) {
            return (trimmed, 1d);
        }

        var tag     = trimmed[..semicolon].Trim();
        var quality = 1d;
        var param   = trimmed[(semicolon + 1)..].Trim();
        if (param.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) {
            if (!double.TryParse(param[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q)
                || !double.IsFinite(q) || q is < 0d or > 1d) {
                // RFC 9110 §12.5.4 restricts qvalue to [0,1]; anything else — including NaN and
                // infinities, which compare false against every bound — disqualifies the member.
                quality = 0d;
            } else {
                quality = q;
            }
        }

        return (tag, quality);
    }
}

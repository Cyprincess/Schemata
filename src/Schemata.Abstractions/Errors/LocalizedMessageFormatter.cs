using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Renders an error template either positionally (<c>{0}</c>, <c>{1}</c>) or by
///     name (<c>{resource}</c>, <c>{path}</c>) from a metadata bag.
/// </summary>
/// <remarks>
///     <para>
///         Named substitution leaves unmatched placeholders literal so a partially
///         populated metadata bag never crashes the call site. Positional fallback
///         delegates to <see cref="string.Format(IFormatProvider, string, object?[])" />
///         and returns the original template when formatting fails so localization
///         never interferes with the developer-facing message.
///     </para>
///     <para>
///         Templates containing both forms are treated as named: positional tokens
///         such as <c>{0}</c> match <c>[A-Za-z_]\w*</c> only when they happen to look
///         alphabetic. Pick one form per template.
///     </para>
/// </remarks>
public static partial class LocalizedMessageFormatter
{
    /// <summary>
    ///     Renders <paramref name="template" /> with <paramref name="args" /> using the
    ///     <see cref="CultureInfo.InvariantCulture" /> formatter.
    /// </summary>
    /// <param name="template">The resx template, or <see langword="null" />.</param>
    /// <param name="args">Optional named arguments keyed by placeholder name.</param>
    /// <returns>The rendered string, or <paramref name="template" /> when formatting fails.</returns>
    public static string? Format(string? template, IReadOnlyDictionary<string, string>? args) {
        return Format(template, args, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Resolves <paramref name="resourceKey" /> from
    ///     <see cref="SchemataResources" /> in
    ///     <see cref="CultureInfo.InvariantCulture" /> and renders it with
    ///     <paramref name="args" />.
    /// </summary>
    /// <param name="resourceKey">The resx data name.</param>
    /// <param name="args">Optional named substitution arguments.</param>
    /// <returns>The English-invariant rendered message.</returns>
    public static string? FormatInvariant(string resourceKey, IReadOnlyDictionary<string, string?>? args = null) {
        var template = SchemataResources.ResourceManager.GetString(resourceKey, CultureInfo.InvariantCulture);
        return Format(template, args is null ? null : NormalizeMetadata(args), CultureInfo.InvariantCulture);
    }

    internal static Dictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string?> args) {
        return args.ToDictionary(kv => kv.Key, kv => kv.Value ?? string.Empty);
    }

    /// <summary>
    ///     Renders <paramref name="template" /> with <paramref name="args" /> using the
    ///     supplied <paramref name="culture" />.
    /// </summary>
    /// <param name="template">The resx template, or <see langword="null" />.</param>
    /// <param name="args">Optional arguments. Named keys drive named substitution;
    ///     otherwise <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.Values" /> is
    ///     enumerated in insertion order for positional <see cref="string.Format(IFormatProvider, string, object?[])" />.
    /// </param>
    /// <param name="culture">Formatter culture for positional substitution.</param>
    /// <returns>The rendered string, or <paramref name="template" /> when formatting fails.</returns>
    public static string? Format(
        string?                              template,
        IReadOnlyDictionary<string, string>? args,
        CultureInfo                          culture
    ) {
        if (string.IsNullOrEmpty(template)) {
            return template;
        }

        if (NamedPlaceholderPattern().IsMatch(template)) {
            return NamedPlaceholderPattern().Replace(template, match => {
                if (args is not null && args.TryGetValue(match.Groups["name"].Value, out var value)) {
                    return value;
                }

                return match.Value;
            });
        }

        if (args is null || args.Count == 0) {
            return template;
        }

        var values = new object?[args.Count];
        var i      = 0;
        foreach (var pair in args) {
            values[i++] = pair.Value;
        }

        try {
            return string.Format(culture, template, values);
        } catch (FormatException) {
            return template;
        }
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled)]
    private static partial Regex NamedPlaceholderPattern();
}

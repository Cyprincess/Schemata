using System;
using System.ComponentModel;
using System.Reflection;
using Schemata.Abstractions.Entities;

namespace Schemata.Common;

/// <summary>Applies and copies the label members of an <see cref="IDescriptive" />.</summary>
public static class DescriptiveExtensions
{
    /// <summary>
    ///     Reads <c>[DisplayName]</c>, <c>[Description]</c> and every <c>[Localized]</c> declared on
    ///     <paramref name="member" /> into <paramref name="target" />. Values already present on the
    ///     target win, so a label assigned in code survives the declaration site.
    /// </summary>
    /// <param name="member">The declaring member carrying the label attributes.</param>
    /// <param name="target">The target receiving the labels.</param>
    public static void ApplyLabels(this MemberInfo member, IDescriptive target) {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(target);

        target.DisplayName ??= member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        target.Description ??= member.GetCustomAttribute<DescriptionAttribute>()?.Description;

        foreach (var localized in member.GetCustomAttributes<LocalizedAttribute>()) {
            if (string.IsNullOrWhiteSpace(localized.Locale)) {
                continue;
            }

            (target.DisplayNames ??= new(StringComparer.OrdinalIgnoreCase)).TryAdd(localized.Locale, localized.DisplayName);

            if (localized.Description is not null) {
                (target.Descriptions ??= new(StringComparer.OrdinalIgnoreCase)).TryAdd(localized.Locale, localized.Description);
            }
        }
    }

    /// <summary>Writes the unlocalized label onto <paramref name="target" />, replacing both members.</summary>
    /// <param name="target">The target receiving the label.</param>
    /// <param name="displayName">Display name in the declaration's own language.</param>
    /// <param name="description">Description in the declaration's own language.</param>
    public static void Label(this IDescriptive target, string displayName, string? description = null) {
        ArgumentNullException.ThrowIfNull(target);

        target.DisplayName = displayName;
        target.Description = description;
    }

    /// <summary>
    ///     Writes the label for one language tag into <paramref name="target" />'s localized maps,
    ///     replacing the entry held for that tag. Ignores a blank <paramref name="locale" />.
    /// </summary>
    /// <param name="target">The target receiving the label.</param>
    /// <param name="locale">IETF BCP 47 language tag, e.g. <c>"zh-Hans"</c>.</param>
    /// <param name="displayName">Display name for <paramref name="locale" />.</param>
    /// <param name="description">Description for <paramref name="locale" />.</param>
    public static void Localize(
        this IDescriptive target,
        string            locale,
        string            displayName,
        string?           description = null
    ) {
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(locale)) {
            return;
        }

        (target.DisplayNames ??= new(StringComparer.OrdinalIgnoreCase))[locale] = displayName;

        if (description is not null) {
            (target.Descriptions ??= new(StringComparer.OrdinalIgnoreCase))[locale] = description;
        }
    }

    /// <summary>
    ///     Writes all four label members of <paramref name="source" /> onto <paramref name="target" />,
    ///     replacing what the target holds. The localized maps are shared, not cloned.
    /// </summary>
    /// <param name="source">The source carrying the labels.</param>
    /// <param name="target">The target receiving the labels.</param>
    public static void CopyLabels(this IDescriptive source, IDescriptive target) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        target.DisplayName  = source.DisplayName;
        target.DisplayNames = source.DisplayNames;
        target.Description  = source.Description;
        target.Descriptions = source.Descriptions;
    }
}

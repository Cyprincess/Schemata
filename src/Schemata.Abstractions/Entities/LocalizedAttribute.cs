using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Declares the label of the annotated target for one language tag, feeding
///     <see cref="IDescriptive.DisplayNames" /> and <see cref="IDescriptive.Descriptions" />. Apply it
///     once per language tag. Stock <c>[DisplayName]</c> and <c>[Description]</c> declare the
///     unlocalized label.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class LocalizedAttribute : Attribute
{
    /// <summary>Declares a label for one language tag.</summary>
    /// <param name="locale">IETF BCP 47 language tag, e.g. <c>"zh-Hans"</c>.</param>
    /// <param name="displayName">Display name for <paramref name="locale" />.</param>
    /// <param name="description">Description for <paramref name="locale" />, when the label carries one.</param>
    public LocalizedAttribute(string locale, string displayName, string? description = null) {
        Locale      = locale;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>Language tag this label applies to.</summary>
    public string Locale { get; }

    /// <summary>Display name for <see cref="Locale" />.</summary>
    public string DisplayName { get; }

    /// <summary>Description for <see cref="Locale" />, or <see langword="null" />.</summary>
    public string? Description { get; }
}

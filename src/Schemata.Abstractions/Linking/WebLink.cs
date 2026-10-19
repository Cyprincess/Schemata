using System.Collections.Generic;

namespace Schemata.Abstractions.Linking;

/// <summary>
///     A link conveyed by an RFC 8288 <c>Link</c> header field: one target, one relation type,
///     and the link's target attributes.
/// </summary>
/// <param name="Target">The link target, resolved against the link context when one was supplied.</param>
/// <param name="Rel">A single relation type, case-normalised to lowercase when parsed.</param>
/// <param name="Anchor">The resolved link context override, or <see langword="null" /> for the representation URL.</param>
/// <param name="Hreflang">The language of the dereferenced target, or <see langword="null" />.</param>
/// <param name="Media">The intended destination medium, or <see langword="null" />.</param>
/// <param name="Title">The human-readable title, or <see langword="null" />.</param>
/// <param name="TitleStar">The RFC 8187 encoded title, or <see langword="null" />.</param>
/// <param name="Type">The media type hint, or <see langword="null" />.</param>
/// <param name="Extensions">The remaining target attributes keyed by their lowercase parameter name.</param>
public sealed record WebLink(
    string Target,
    string Rel,
    string? Anchor = null,
    string? Hreflang = null,
    string? Media = null,
    string? Title = null,
    string? TitleStar = null,
    string? Type = null,
    IReadOnlyDictionary<string, string>? Extensions = null);

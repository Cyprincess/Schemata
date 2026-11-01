using System;
using System.Collections.Generic;
using Schemata.Abstractions.Linking;
using Schemata.Common.Linking;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     The single RFC 8288 <c>Link</c> header field projection of <see cref="WebLink" /> values.
/// </summary>
public static class LinkHeader
{
    /// <summary>
    ///     The HTTP field name.
    /// </summary>
    public const string Name = "Link";

    /// <summary>
    ///     Parses a <c>Link</c> header field value; see <see cref="LinkHeaderParser.Parse" />.
    /// </summary>
    /// <param name="value">The field value.</param>
    /// <param name="context">The URL of the representation carrying the header.</param>
    /// <returns>The parsed links.</returns>
    public static IReadOnlyList<WebLink> Parse(string? value, Uri? context = null) => LinkHeaderParser.Parse(value, context);

    /// <summary>
    ///     Serializes links into one <c>Link</c> header field value.
    /// </summary>
    /// <param name="links">The links to serialize; duplicates are emitted once.</param>
    /// <returns>The field value, or an empty string when there are no links.</returns>
    public static string Serialize(IEnumerable<WebLink>? links) => LinkValue.Serialize(links);
}

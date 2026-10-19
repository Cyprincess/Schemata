using System.Collections.Generic;
using Schemata.Abstractions.Linking;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     Contributes links for the current HTTP response. The shared result shaping collects every
///     registered contributor and serializes the links once.
/// </summary>
public interface ILinkContributor
{
    /// <summary>
    ///     Returns the links for the supplied response state.
    /// </summary>
    /// <param name="context">The request and result being shaped.</param>
    /// <returns>The contributed links.</returns>
    IEnumerable<WebLink> GetLinks(LinkContext context);
}

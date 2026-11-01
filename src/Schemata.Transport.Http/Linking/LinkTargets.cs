using System;
using System.Linq;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     Encoding for link targets assembled from persisted resource names.
/// </summary>
public static class LinkTargets
{
    /// <summary>
    ///     Percent-encodes every path segment while keeping the <c>/</c> separators.
    /// </summary>
    /// <param name="path">The path or canonical resource name.</param>
    /// <returns>The URI-path representation of the path.</returns>
    public static string EncodePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}

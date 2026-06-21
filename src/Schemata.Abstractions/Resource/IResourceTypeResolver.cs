using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Resolves a registered resource entity <see cref="Type" /> from a runtime resource-name
///     string. The reverse of <c>ResourceNameDescriptor</c> (which maps a known type to its name):
///     given an AIP-122 canonical name or a bare collection segment, returns the entity type that
///     owns it, or <see langword="null" /> when no registered resource matches.
/// </summary>
public interface IResourceTypeResolver
{
    /// <summary>
    ///     Resolves an entity type from a full canonical name (e.g. <c>"publishers/acme/books/x"</c>)
    ///     by matching it against every registered resource name pattern. Falls back to a bare
    ///     collection segment (e.g. <c>"books"</c>).
    /// </summary>
    /// <param name="canonicalName">The canonical resource name or collection segment.</param>
    /// <returns>The owning entity type, or <see langword="null" /> when none matches.</returns>
    Type? Resolve(string canonicalName);

    /// <summary>
    ///     Resolves an entity type from a bare collection segment (e.g. <c>"books"</c>).
    /// </summary>
    /// <param name="collection">The collection segment of a resource name.</param>
    /// <returns>The owning entity type, or <see langword="null" /> when none matches.</returns>
    Type? ResolveCollection(string collection);
}

using System;
using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

/// <summary>
///     The resources explicitly registered with the resource system, and the AIP-136 custom methods
///     each one declares. Populated during service registration and read-only from then on.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>Every registered resource descriptor, in registration order.</summary>
    IReadOnlyList<ResourceAttribute> Resources { get; }

    /// <summary>Returns the descriptor registered for <paramref name="entity" />, or <see langword="null" />.</summary>
    ResourceAttribute? GetResource(Type entity);

    /// <summary>
    ///     Returns the AIP-136 custom methods declared for <paramref name="entity" />, deduplicated by
    ///     <see cref="ResourceMethodAttribute.Verb" />. Empty when the entity declares none.
    /// </summary>
    IReadOnlyList<ResourceMethodAttribute> GetMethods(Type entity);
}

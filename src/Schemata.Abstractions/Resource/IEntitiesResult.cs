using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks an operation result whose <c>Entities</c> property carries resources of
///     <typeparamref name="TItem" />. Transport layers rewrite that property's wire name to
///     the resource plural resolved from <typeparamref name="TItem" /> per
///     <seealso href="https://google.aip.dev/132">AIP-132</seealso> and
///     <seealso href="https://google.aip.dev/231">AIP-231..235</seealso>.
/// </summary>
/// <typeparam name="TItem">The resource DTO type carried by the result.</typeparam>
public interface IEntitiesResult<TItem>
{
    /// <summary>
    ///     Resource items carried by the result.
    /// </summary>
    IList<TItem>? Entities { get; set; }
}

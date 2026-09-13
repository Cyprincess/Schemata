using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks an operation result whose <c>Entities</c> property carries resources of
///     <typeparamref name="TItem" /> for the resource identified by <typeparamref name="TEntity" />.
///     Transport layers rewrite that property's wire name to the resource plural resolved from
///     <typeparamref name="TEntity" /> per
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type carrying the resource identity.</typeparam>
/// <typeparam name="TItem">The resource DTO type carried by the result.</typeparam>
public interface IEntitiesResult<TEntity, TItem>
{
    /// <summary>
    ///     Resource items carried by the result.
    /// </summary>
    IList<TItem>? Entities { get; set; }
}

using System.Collections.Generic;

namespace Schemata.Entity.Repository;

/// <summary>
///     Describes the entities included in a completed repository commit.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
public sealed class CommitChanges<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     The entities added by the commit.
    /// </summary>
    public IReadOnlyList<TEntity> Added { get; init; } = [];

    /// <summary>
    ///     The entities updated by the commit.
    /// </summary>
    public IReadOnlyList<TEntity> Updated { get; init; } = [];

    /// <summary>
    ///     The entities removed by the commit.
    /// </summary>
    public IReadOnlyList<TEntity> Removed { get; init; } = [];
}

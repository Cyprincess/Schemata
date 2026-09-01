using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Actor.Skeleton.Entities;

/// <summary>
///     Persisted state row for an <see cref="IPersistentActor" />, read and written through
///     <c>IRepository&lt;SchemataActor&gt;</c>. Holds only the actor's opaque serialized state —
///     never the authoritative domain data, which stays in its own entity and is reloaded fresh
///     each turn.
/// </summary>
[Table("SchemataActors")]
[CanonicalName("actors/{actor}")]
[PrimaryKey(nameof(Uid))]
public class SchemataActor : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>
    ///     The serialized state last produced by <see cref="IPersistentActor.SaveStateAsync" />,
    ///     opaque to the framework. <see langword="null" /> until the actor's first successful turn.
    /// </summary>
    public virtual byte[]? State { get; set; }

    #region ICanonicalName Members

    /// <summary>
    ///     The owning actor's <see cref="ActorId" /> rendered via <see cref="ActorId.ToString" />
    ///     (<c>"{Type}/{Key}"</c>) rather than a framework-generated slug — an actor's identity
    ///     already is its <see cref="ActorId" />.
    /// </summary>
    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}

using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Event.Skeleton;

namespace Schemata.Domain.Skeleton;

/// <summary>
///     Convenience base for a user-written aggregate: carries the identity and concurrency traits and
///     buffers raised events until the unit of work commits.
/// </summary>
/// <remarks>
///     Optional. An entity can implement <see cref="IAggregateRoot" /> and
///     <see cref="IHasPendingEvents" /> directly; this class exists so the common case does not have
///     to restate the buffer. Schemata's own entities do not derive from it.
/// </remarks>
public abstract class AggregateBase : IAggregateRoot, IHasPendingEvents
{
    private readonly List<IEvent> _pending = [];

    #region IAggregateRoot Members

    /// <inheritdoc />
    public virtual Guid Uid { get; set; }

    /// <inheritdoc />
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IHasPendingEvents Members

    /// <inheritdoc />
    public IReadOnlyList<IEvent> DequeuePendingEvents() {
        if (_pending.Count == 0) {
            return [];
        }

        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }

    #endregion

    /// <summary>Buffers <paramref name="event" /> for publication after the commit succeeds.</summary>
    /// <remarks>
    ///     Buffering rather than publishing is the point: an event raised by a transaction that later
    ///     rolls back must never reach a subscriber.
    /// </remarks>
    /// <param name="event">The event the aggregate produced.</param>
    protected void Raise(IEvent @event) { _pending.Add(@event); }
}

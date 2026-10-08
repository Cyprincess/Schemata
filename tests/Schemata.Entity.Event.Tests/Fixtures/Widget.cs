using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Event.Skeleton;

namespace Schemata.Entity.Event.Tests.Fixtures;

/// <summary>
///     Buffers events without implementing any aggregate marker — the flush mechanism is
///     deliberately available to plain entities, not only to DDD aggregates.
/// </summary>
public sealed class Widget : IHasPendingEvents, ICanonicalName
{
    private readonly List<IEvent> _pending = [];

    #region IHasPendingEvents Members

    public IReadOnlyList<IEvent> DequeuePendingEvents() {
        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }

    #endregion
    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    public void Rename(string name) { _pending.Add(new WidgetRenamed(name)); }
}

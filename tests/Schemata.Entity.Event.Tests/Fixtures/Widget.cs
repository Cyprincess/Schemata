using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Entity.Event.Tests.Fixtures;

/// <summary>
///     Buffers events without implementing any aggregate marker — the flush mechanism is
///     deliberately available to plain entities, not only to DDD aggregates.
/// </summary>
/// <remarks>
///     Public, and in its own file, because Moq has to build an <c>IRepository&lt;Widget&gt;</c>
///     proxy: Castle DynamicProxy cannot see a type nested privately in the test class.
/// </remarks>
public sealed class Widget : IHasPendingEvents
{
    private readonly List<IEvent> _pending = [];

    #region IHasPendingEvents Members

    public IReadOnlyList<IEvent> DequeuePendingEvents() {
        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }

    #endregion

    public void Rename(string name) { _pending.Add(new WidgetRenamed(name)); }
}
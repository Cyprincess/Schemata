using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Entity.Event.Tests.Fixtures;

/// <summary>A domain event carrying enough state to tell two raises apart.</summary>
public sealed record WidgetRenamed(string Name) : IEvent;

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

/// <summary>An entity that buffers nothing, so the advisor must leave it alone.</summary>
public sealed class Plain
{
    public string Name { get; init; } = string.Empty;
}

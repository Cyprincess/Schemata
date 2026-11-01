using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A collection of graph members that forbids mutation once frozen. The collection itself
///     freezes each member on <see cref="Freeze" />, then guards every insertion, replacement,
///     removal and clear path against further mutation.
/// </summary>
/// <remarks>
///     No <c>where T : FlowGraphNode</c> constraint because some callers store interface
///     references such as <c>IEventDefinition</c> that may point at user-defined implementations;
///     freeze recurses only into items that actually inherit <see cref="FlowGraphNode" />.
/// </remarks>
public sealed class FlowGraphCollection<T> : Collection<T>, IReadOnlyList<T>
{
    /// <summary>True after <see cref="Freeze" /> has run.</summary>
    [JsonIgnore]
    public bool IsFrozen { get; private set; }

    /// <summary>Adds a range of items. Convenience for graph construction; throws once frozen.</summary>
    public void AddRange(IEnumerable<T> items) {
        if (items is null) {
            throw new ArgumentNullException(nameof(items));
        }

        foreach (var item in items) {
            Add(item);
        }
    }

    /// <summary>Removes every item matching <paramref name="match" />. Throws once frozen.</summary>
    public int RemoveAll(Predicate<T> match) {
        if (match is null) {
            throw new ArgumentNullException(nameof(match));
        }

        var removed = 0;
        for (var i = Count - 1; i >= 0; i--) {
            if (match(this[i])) {
                RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    ///     Marks the collection as frozen and freezes every member that is a
    ///     <see cref="FlowGraphNode" />. Idempotent and cycle-safe via
    ///     <see cref="FlowGraphNode.Freeze" />.
    /// </summary>
    public void Freeze() {
        if (IsFrozen) {
            return;
        }

        IsFrozen = true;
        foreach (var item in this) {
            if (item is FlowGraphNode node) {
                node.Freeze();
            }
        }
    }

    private void EnsureMutable() {
        if (IsFrozen) {
            throw new InvalidOperationException(
                $"Cannot modify '{typeof(FlowGraphCollection<T>).Name}' after the owning process definition has been frozen.");
        }
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, T item) {
        EnsureMutable();
        base.InsertItem(index, item);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, T item) {
        EnsureMutable();
        base.SetItem(index, item);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index) {
        EnsureMutable();
        base.RemoveItem(index);
    }

    /// <inheritdoc />
    protected override void ClearItems() {
        EnsureMutable();
        base.ClearItems();
    }
}

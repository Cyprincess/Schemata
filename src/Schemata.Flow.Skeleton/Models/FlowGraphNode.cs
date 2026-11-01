using System;
using System.Text.Json.Serialization;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Base type for nodes that participate in the BPMN process graph freeze contract.
///     Once <see cref="Freeze" /> runs, every guarded setter on the node throws.
/// </summary>
public abstract class FlowGraphNode
{
    private bool _frozen;

    /// <summary>Whether <see cref="Freeze" /> has run on this node.</summary>
    [JsonIgnore]
    public bool IsFrozen => _frozen;

    /// <summary>
    ///     Marks the node as frozen and traverses its owned graph children via
    ///     <see cref="FreezeCore" />. Idempotent and cycle-safe: revisiting a node whose
    ///     <see cref="Freeze" /> already returned is a no-op.
    /// </summary>
    public void Freeze() {
        if (_frozen) {
            return;
        }

        _frozen = true;
        FreezeCore();
    }

    /// <summary>
    ///     Throws <see cref="InvalidOperationException" /> when the node is frozen. Subclasses
    ///     call this from <c>set</c> accessors of every executable graph property.
    /// </summary>
    protected internal void EnsureMutable() {
        if (_frozen) {
            throw new InvalidOperationException(
                $"Cannot modify '{GetType().Name}' after the owning process definition has been frozen.");
        }
    }

    /// <summary>
    ///     Subclasses freeze every owned graph child with explicit typed traversal so cycle
    ///     handling stays uniform across all graph types. The base implementation does nothing.
    /// </summary>
    protected internal virtual void FreezeCore() { }
}

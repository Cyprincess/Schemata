using System.Collections.Generic;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>
///     Scope-local LIFO container for BPMN compensation handlers.
/// </summary>
/// <remarks>
///     The BPMN engine owns one stack per scope and runs each scope on a single execution path, so this
///     container does not synchronize access. Handler registrations are runtime-only and are
///     not rehydrated after a process restart.
/// </remarks>
public sealed class CompensationStack
{
    private readonly List<ICompensationHandler> _handlers = [];

    /// <summary>Initializes an empty compensation stack.</summary>
    public CompensationStack() { }

    /// <summary>Number of handlers currently registered in this scope.</summary>
    public int Count => _handlers.Count;

    /// <summary>Registers a handler on the top of the stack.</summary>
    /// <param name="handler">The handler to register.</param>
    public void Register(ICompensationHandler handler) { _handlers.Add(handler); }

    /// <summary>Returns registered handlers from bottom to top, preserving insertion order.</summary>
    public IReadOnlyList<ICompensationHandler> Snapshot() { return [.. _handlers]; }

    /// <summary>Removes a registered handler from the stack.</summary>
    /// <param name="handler">The handler to remove.</param>
    /// <returns><see langword="true" /> when the handler was present.</returns>
    public bool Remove(ICompensationHandler handler) { return _handlers.Remove(handler); }

    /// <summary>Drops every handler registered for this scope.</summary>
    public void Clear() { _handlers.Clear(); }
}

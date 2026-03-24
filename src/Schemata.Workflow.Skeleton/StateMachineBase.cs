using System;
using Automatonymous;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton;

/// <summary>
/// Base class for defining state machines that drive workflow transitions on stateful entities.
/// </summary>
/// <typeparam name="TI">The stateful entity type managed by this state machine.</typeparam>
/// <remarks>
/// Subclass this to define states, events, and transition rules using the Automatonymous DSL.
/// The state machine is resolved from DI and used by <see cref="Managers.SchemataWorkflowManager{TWorkflow,TTransition,TResponse}"/>
/// to raise events and query state graphs.
/// </remarks>
public abstract class StateMachineBase<TI> : AutomatonymousStateMachine<TI>, IDisposable
    where TI : class, IStatefulEntity
{
    private readonly IDisposable? _eventObserver;
    private readonly IDisposable? _stateObserver;
    private          bool         _disposed;

    /// <summary>
    /// Initializes a new instance without observers.
    /// </summary>
    protected StateMachineBase() { }

    /// <summary>
    /// Initializes a new instance with a state observer.
    /// </summary>
    /// <param name="observer">The observer notified on state changes.</param>
    protected StateMachineBase(StateObserver<TI> observer) { _stateObserver = this.ConnectStateObserver(observer); }

    /// <summary>
    /// Initializes a new instance with both event and state observers.
    /// </summary>
    /// <param name="eventObserver">The observer notified when events are raised.</param>
    /// <param name="stateObserver">The observer notified on state changes.</param>
    protected StateMachineBase(EventObserver<TI> eventObserver, StateObserver<TI> stateObserver) {
        _eventObserver = this.ConnectEventObserver(eventObserver);
        _stateObserver = this.ConnectStateObserver(stateObserver);
    }

    #region IDisposable Members

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    /// <summary>
    /// Gets a named event that carries data of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The event data type.</typeparam>
    /// <param name="name">The event name as defined in the state machine.</param>
    /// <returns>The matching <see cref="Event{T}"/>.</returns>
    public Event<T> GetEvent<T>(string name) { return Event<T>(name); }

    /// <summary>
    /// Gets the current state of the given entity instance.
    /// </summary>
    /// <param name="instance">The stateful entity to inspect.</param>
    /// <returns>The current <see cref="State{TI}"/>.</returns>
    public State<TI> GetCurrentState(TI instance) { return GetState(instance.State); }

    /// <summary>
    /// Releases resources held by the state machine observers.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources.</param>
    protected virtual void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            _eventObserver?.Dispose();
            _stateObserver?.Dispose();
        }

        _disposed = true;
    }
}

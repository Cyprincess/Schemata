using System;
using Automatonymous;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton;

public abstract class StateMachineBase<TI> : AutomatonymousStateMachine<TI>, IDisposable
    where TI : class, IStatefulEntity
{
    private readonly IDisposable? _eventObserver;
    private readonly IDisposable? _stateObserver;
    private          bool         _disposed;

    protected StateMachineBase() { }

    protected StateMachineBase(StateObserver<TI> observer) {
        _stateObserver = this.ConnectStateObserver(observer);
    }

    protected StateMachineBase(EventObserver<TI> eventObserver, StateObserver<TI> stateObserver) {
        _eventObserver = this.ConnectEventObserver(eventObserver);
        _stateObserver = this.ConnectStateObserver(stateObserver);
    }

    #region IDisposable Members

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    public Event<T> GetEvent<T>(string name) {
        return Event<T>(name);
    }

    public State<TI> GetCurrentState(TI instance) {
        return GetState(instance.State);
    }

    protected virtual void Dispose(bool disposing) {
        if (_disposed) return;

        if (disposing) {
            _eventObserver?.Dispose();
            _stateObserver?.Dispose();
        }

        _disposed = true;
    }
}

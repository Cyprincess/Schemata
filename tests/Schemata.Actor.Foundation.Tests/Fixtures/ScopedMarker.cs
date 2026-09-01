using System;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>A scoped, disposable marker service: a fresh instance per DI scope, observable disposal.</summary>
public sealed class ScopedMarker : IDisposable
{
    public ScopedMarker(MarkerRegistry registry) {
        registry.Instances.Add(this);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public bool Disposed { get; private set; }

    #region IDisposable Members

    public void Dispose() => Disposed = true;

    #endregion
}
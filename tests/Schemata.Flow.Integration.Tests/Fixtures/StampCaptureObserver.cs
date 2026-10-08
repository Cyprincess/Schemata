using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

/// <summary>Records the process stamp carried by each lifecycle notification, keyed by process canonical name.</summary>
public sealed class StampCaptureObserver : IProcessLifecycleObserver
{
    public Dictionary<string, Guid> Started      { get; } = new();
    public Dictionary<string, Guid> Transitioned { get; } = new();
    public Dictionary<string, Guid> Terminated   { get; } = new();
    public Dictionary<string, Guid> Failed       { get; } = new();

    #region IProcessLifecycleObserver Members

    public Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default) {
        Started[process.CanonicalName!] = process.Timestamp;
        return Task.CompletedTask;
    }

    public Task OnTransitionedAsync(SchemataProcess process, SchemataProcessTransition transition, CancellationToken ct = default) {
        Transitioned[process.CanonicalName!] = process.Timestamp;
        return Task.CompletedTask;
    }

    public Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default) {
        Terminated[process.CanonicalName!] = process.Timestamp;
        return Task.CompletedTask;
    }

    public Task OnFailedAsync(SchemataProcess process, Exception exception, CancellationToken ct = default) {
        Failed[process.CanonicalName!] = process.Timestamp;
        return Task.CompletedTask;
    }

    #endregion
}

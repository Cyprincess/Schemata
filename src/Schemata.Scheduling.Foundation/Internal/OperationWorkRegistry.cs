using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Internal;

internal sealed class OperationWorkRegistry
{
    private readonly ConcurrentDictionary<Guid, Func<IServiceProvider, JobContext, CancellationToken, Task>> _work = [];

    public void Register(Guid uid, Func<IServiceProvider, JobContext, CancellationToken, Task> work) {
        if (!_work.TryAdd(uid, work)) {
            throw new InvalidOperationException($"Operation work item '{uid:N}' is already registered.");
        }
    }

    public void Remove(Guid uid) { _work.TryRemove(uid, out _); }

    public bool TryRemove(Guid uid, out Func<IServiceProvider, JobContext, CancellationToken, Task> work) {
        return _work.TryRemove(uid, out work!);
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Internal;

internal sealed class OperationJob(
    IServiceProvider      services,
    OperationWorkRegistry registry
) : IScheduledJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        if (context.ExecutionUid is not { } uid || !registry.TryRemove(uid, out var work)) {
            throw new InvalidOperationException("Operation work item is no longer registered.");
        }

        await work(services, context, ct);
    }
}
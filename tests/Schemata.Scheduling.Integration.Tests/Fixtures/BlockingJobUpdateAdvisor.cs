using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class BlockingJobUpdateAdvisor : IRepositoryUpdateAdvisor<SchemataJob>
{
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _armed;
    private int           _updateCount;

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int UpdateCount => Volatile.Read(ref _updateCount);

    public void Arm() { _armed = true; }

    public void Release() { _release.TrySetResult(); }

    #region IRepositoryUpdateAdvisor<SchemataJob> Members

    public int Order => 50_000_000;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext            ctx,
        IRepository<SchemataJob> repository,
        SchemataJob              entity,
        CancellationToken        ct
    ) {
        if (!_armed) {
            return AdviseResult.Continue;
        }

        if (Interlocked.Increment(ref _updateCount) == 1) {
            Entered.TrySetResult();
            await _release.Task.WaitAsync(BarrierTimeout, ct);
        }

        return AdviseResult.Continue;
    }

    #endregion
}

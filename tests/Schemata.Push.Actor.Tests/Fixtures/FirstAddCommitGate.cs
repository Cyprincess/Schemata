using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Actor.Tests.Fixtures;

/// <summary>
///     Parks the first push-subscription add after its uniqueness lookup passed and before its
///     commit, so a test can commit a same-key row inside that window and observe the provider's
///     own constraint error on the parked add — the optimistic-uniqueness race the actor bridge
///     exists to serialize.
/// </summary>
internal sealed class FirstAddCommitGate : IRepositoryAddAdvisor<SchemataPushSubscription>
{
    private readonly TaskCompletionSource<bool> _holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the first add is parked between lookup and commit.</summary>
    public Task Holding => _holding.Task;

    /// <summary>Lets the parked add commit.</summary>
    public void Release() => _release.TrySetResult(true);

    public int Order => AdviceAddUniqueness.DefaultOrder + 1;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                            ctx,
        IRepository<SchemataPushSubscription>    repository,
        SchemataPushSubscription                 entity,
        CancellationToken                        ct = default
    ) {
        if (!_holding.TrySetResult(true)) {
            return AdviseResult.Continue;
        }

        await _release.Task;

        return AdviseResult.Continue;
    }
}

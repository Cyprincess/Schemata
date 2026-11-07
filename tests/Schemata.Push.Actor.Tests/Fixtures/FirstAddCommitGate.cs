using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Actor.Tests.Fixtures;

/// <summary>
///     Parks the first push-subscription add after its uniqueness lookup passed and before its
///     commit, while its serializable transaction holds the provider write lock. A second add can
///     then observe the lock conflict before the parked add completes.
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
        MutationOptions      options,
        CancellationToken                        ct = default
    ) {
        if (!_holding.TrySetResult(true)) {
            return AdviseResult.Continue;
        }

        await _release.Task;

        return AdviseResult.Continue;
    }
}

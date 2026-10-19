using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Report.Skeleton.Advisors;

namespace Schemata.Report.Actor.Tests.Fixtures;

/// <summary>
///     Parks the first report generation at snapshot finalization — after its chunks are
///     committed, before its header turns Succeeded — so a test can hold one generation
///     mid-write while a second one runs to completion against the same report.
/// </summary>
public sealed class SnapshotFinalizationGate : IReportSnapshotAdvisor
{
    private readonly TaskCompletionSource<bool> _holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the first generation is parked at finalization.</summary>
    public Task Holding => _holding.Task;

    /// <summary>Lets the parked generation finalize.</summary>
    public void Release() => _release.TrySetResult(true);

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        ReportSnapshotContext context,
        CancellationToken     ct = default
    ) {
        if (!_holding.TrySetResult(true)) {
            return AdviseResult.Continue;
        }

        await _release.Task;

        return AdviseResult.Continue;
    }
}

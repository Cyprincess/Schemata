using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

/// <summary>
///     Review → Call, where entering Call performs an external effect through
///     <see cref="FakeExternalSystem" /> following the recovery protocol the system's capabilities allow:
///     outcome query when supported, otherwise a re-issue under the same request id, and an unknown-outcome
///     report when the response is lost with neither query nor deduplication available.
/// </summary>
public sealed class ExternalEffectProcess : ProcessDefinition
{
    public ExternalEffectProcess() {
        BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Go(Call);
        this.During(Call).OnEnter<Order>(CallExternalAsync).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Call   { get; } = null!;

    private static ValueTask CallExternalAsync(FlowTaskContext context, Order order, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var external  = context.GetRequiredService<FakeExternalSystem>();
        var requestId = context.RequestId!;

        var confirmed = external.Queryable && external.QueryApplied(requestId);
        if (!confirmed) {
            try {
                external.Send(requestId);
            } catch (TimeoutException) {
                context.ReportOutcomeUnknown();
            }
        }

        external.SimulateCrashAfterCall(context);
        return ValueTask.CompletedTask;
    }
}

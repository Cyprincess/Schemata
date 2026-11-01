using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Provider seam for durable external-effect intent and completion records, consumed by the flow
///     engines around every <see cref="Schemata.Flow.Skeleton.Models.ProcedureTaskBase" /> body invocation.
///     Install one recorder through dependency injection; with no recorder installed the engines still
///     generate and forward <see cref="FlowTaskContext.RequestId" /> but keep no records.
/// </summary>
/// <remarks>
///     <para>
///         The recorder writes through the transition's unit of work: enlist store access with
///         <see cref="IRepository.Join" /> against the supplied <see cref="IUnitOfWork" /> so the intent
///         commits with the transition and disappears when the transition rolls back.
///     </para>
///     <para>
///         A record rolled back with its transition leaves no local trace of the external call, so recovery
///         is the handler's protocol: query the external system by <see cref="FlowEffectIntent.RequestId" />
///         when the protocol supports outcome query, otherwise re-issue the call with the same identifier
///         and let the receiver deduplicate. When the external system supports neither, the handler reports
///         the outcome through <see cref="FlowTaskContext.ReportOutcomeUnknown" /> and the engine surfaces
///         it instead of re-executing or skipping. The framework never claims exactly-once external effects.
///     </para>
/// </remarks>
public interface IFlowEffectRecorder
{
    /// <summary>
    ///     Counts the intent records already persisted for <paramref name="token" />. The engine adds the
    ///     count to the token's in-transition effect sequence to derive the ordinal in the next
    ///     <see cref="FlowEffectIntent.RequestId" />.
    /// </summary>
    /// <param name="token">Canonical name of the token performing the effect.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<long> CountIntentsAsync(string token, CancellationToken ct);

    /// <summary>
    ///     Records <paramref name="intent" /> in the transition's unit of work and returns the authoritative
    ///     record for <see cref="FlowEffectIntent.RequestId" />: the stored record when one already exists,
    ///     otherwise the newly recorded intent.
    /// </summary>
    /// <param name="intent">The intent to record.</param>
    /// <param name="unitOfWork">The unit of work the current transition commits through.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<FlowEffectIntent> RecordIntentAsync(FlowEffectIntent intent, IUnitOfWork unitOfWork, CancellationToken ct);

    /// <summary>
    ///     Settles the intent identified by <paramref name="requestId" /> as
    ///     <see cref="FlowEffectState.Completed" /> after the task body returned normally.
    /// </summary>
    /// <param name="requestId">The request identifier the intent was recorded with.</param>
    /// <param name="unitOfWork">The unit of work the current transition commits through.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RecordCompletionAsync(string requestId, IUnitOfWork unitOfWork, CancellationToken ct);

    /// <summary>
    ///     Settles the intent identified by <paramref name="requestId" /> as
    ///     <see cref="FlowEffectState.OutcomeUnknown" /> after the task handler reported an indeterminate
    ///     external outcome. When the transition rolls back this write rolls back with it, and the unknown
    ///     outcome surfaces through the thrown exception instead.
    /// </summary>
    /// <param name="requestId">The request identifier the intent was recorded with.</param>
    /// <param name="unitOfWork">The unit of work the current transition commits through.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RecordOutcomeUnknownAsync(string requestId, IUnitOfWork unitOfWork, CancellationToken ct);
}

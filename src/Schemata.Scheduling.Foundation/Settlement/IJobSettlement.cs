using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Settlement;

/// <summary>
///     The scheduling-owned settlement boundary: records the terminal execution state together
///     with the job row's post-fire result and its next schedule in one transaction when the
///     execution and job stores can share a unit of work.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
internal interface IJobSettlement<TJob, TExecution>
    where TJob : SchemataJob
    where TExecution : SchemataJobExecution
{
    Task<SettlementOutcome> SettleAsync(
        JobExecutionLease<TJob, TExecution> lease,
        JobExecutionResult<TJob>            result,
        CancellationToken                   ct = default);
}

/// <summary>Outcome of one settlement attempt.</summary>
/// <param name="ExecutionSettled">The claimed execution row reached its terminal state.</param>
/// <param name="ScheduleSettled">
///     The job row and its pending occurrence were persisted, or no persisted schedule required a write.
/// </param>
/// <param name="Failure">The first failure that kept a part of the settlement from committing.</param>
internal readonly record struct SettlementOutcome(bool ExecutionSettled, bool ScheduleSettled, Exception? Failure);

/// <summary>Terminal result the dispatcher hands to the settlement owner.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <param name="Job">The in-memory job whose post-fire view observers receive.</param>
/// <param name="State">Terminal execution state.</param>
/// <param name="EndTime">Completion instant recorded on the execution row.</param>
/// <param name="Error">Job-body failure message, or <see langword="null" />.</param>
internal sealed record JobExecutionResult<TJob>(
    TJob           Job,
    ExecutionState State,
    DateTime       EndTime,
    string?        Error)
    where TJob : SchemataJob;

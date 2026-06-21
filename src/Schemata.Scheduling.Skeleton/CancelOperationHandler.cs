using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     AIP-136 <c>:cancel</c> handler on <see cref="SchemataJobExecution" />.
///     Transitions the row to <see cref="ExecutionState.Cancelled" /> and asks
///     <see cref="IScheduler.UnscheduleAsync" /> to drop the in-memory entry;
///     terminal executions return <c>FAILED_PRECONDITION</c>.
/// </summary>
public sealed class CancelOperationHandler(
    IRepository<SchemataJobExecution> executions,
    IScheduler                        scheduler,
    TimeProvider?                     time = null
) : IResourceMethodHandler<SchemataJobExecution, EmptyResourceRequest, Operation>
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IResourceMethodHandler<SchemataJobExecution, EmptyResourceRequest, Operation> Members

    public async ValueTask<Operation> InvokeAsync(
        string?               name,
        EmptyResourceRequest  request,
        SchemataJobExecution? entity,
        ClaimsPrincipal?      principal,
        CancellationToken     ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.State is ExecutionState.Succeeded
                         or ExecutionState.Failed
                         or ExecutionState.Cancelled) {
            throw new FailedPreconditionException(
                message: $"Operation '{entity.CanonicalName}' has already finished.");
        }

        if (!string.IsNullOrEmpty(entity.Job)) {
            await scheduler.UnscheduleAsync(entity.Job, ct);
        }

        entity.State   = ExecutionState.Cancelled;
        entity.EndTime = _time.GetUtcNow().UtcDateTime;
        await executions.UpdateAsync(entity, ct);
        await executions.CommitAsync(ct);

        return OperationMapper.FromExecution(entity);
    }

    #endregion
}

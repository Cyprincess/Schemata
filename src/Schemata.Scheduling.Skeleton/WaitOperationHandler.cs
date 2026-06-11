using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     AIP-151 <c>:wait</c> handler on <see cref="SchemataJobExecution" />.
///     Performs server-side bounded polling capped at 30 seconds and returns the
///     current snapshot once the row reaches a terminal state or the deadline
///     elapses.
/// </summary>
public sealed class WaitOperationHandler(IRepository<SchemataJobExecution> executions)
    : IResourceMethodHandler<SchemataJobExecution, WaitOperationRequest, SchemataOperation>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly  TimeSpan MaxWait      = TimeSpan.FromSeconds(30);

    #region IResourceMethodHandler<SchemataJobExecution, WaitOperationRequest, SchemataOperation> Members

    public async ValueTask<SchemataOperation> InvokeAsync(
        string?               name,
        WaitOperationRequest  request,
        SchemataJobExecution? entity,
        ClaimsPrincipal?      principal,
        CancellationToken     ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        if (IsTerminal(entity.State)) {
            return SchemataOperation.FromExecution(entity);
        }

        var deadline = DateTime.UtcNow + GetEffectiveTimeout(request.Timeout);
        var uid      = entity.Uid;

        while (DateTime.UtcNow < deadline) {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero) {
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval, ct);
            }

            var snapshot = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                q => q.Where(e => e.Uid == uid), ct);

            if (snapshot is null) {
                return SchemataOperation.FromExecution(entity);
            }

            if (IsTerminal(snapshot.State)) {
                return SchemataOperation.FromExecution(snapshot);
            }

            entity = snapshot;
        }

        return SchemataOperation.FromExecution(entity);
    }

    #endregion

    private static bool IsTerminal(ExecutionState state) {
        return state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;
    }

    public static TimeSpan GetEffectiveTimeout(TimeSpan? requested) {
        if (requested is null || requested.Value <= TimeSpan.Zero) {
            return MaxWait;
        }

        return requested.Value < MaxWait ? requested.Value : MaxWait;
    }
}

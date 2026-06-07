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
    : IResourceMethodHandler<SchemataJobExecution, EmptyResourceRequest, SchemataJobExecution>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxWait      = TimeSpan.FromSeconds(30);

    #region IResourceMethodHandler<SchemataJobExecution, EmptyResourceRequest, SchemataJobExecution> Members

    public async ValueTask<SchemataJobExecution> InvokeAsync(
        string?              name,
        EmptyResourceRequest request,
        SchemataJobExecution entity,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        if (IsTerminal(entity.State)) {
            return entity;
        }

        var deadline = DateTime.UtcNow + MaxWait;
        var uid      = entity.Uid;

        while (DateTime.UtcNow < deadline) {
            await Task.Delay(PollInterval, ct);

            var snapshot = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                q => q.Where(e => e.Uid == uid), ct);

            if (snapshot is null) {
                return entity;
            }

            if (IsTerminal(snapshot.State)) {
                return snapshot;
            }

            entity = snapshot;
        }

        return entity;
    }

    #endregion

    private static bool IsTerminal(ExecutionState state) {
        return state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;
    }
}

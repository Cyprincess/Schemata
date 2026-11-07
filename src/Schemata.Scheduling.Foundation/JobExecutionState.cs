using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Default <see cref="IJobExecutionState" /> over the host's persisted execution entity. Each
///     read restores the execution's message context in a fresh scope before querying, matching the
///     dispatch and settlement paths.
/// </summary>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
internal sealed class JobExecutionState<TExecution>(
    IMessageExecutionScopeRunner runner
) : IJobExecutionState
    where TExecution : SchemataJobExecution
{
    public async ValueTask<bool> IsCancelledAsync(Guid uid, MessageContext message, CancellationToken ct = default) {
        return await runner.RunAsync(message, async (services, token) => {
            var repository = services.GetRequiredService<IRepository<TExecution>>();
            var execution = await repository.FirstOrDefaultAsync(query => query.Where(row => row.Uid == uid), token);
            return execution?.State is ExecutionState.Cancelled;
        }, ct);
    }
}

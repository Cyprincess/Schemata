using System;
using System.Linq;
using System.Runtime.ExceptionServices;
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
    IMessageExecutionScopeFactory scopes
) : IJobExecutionState
    where TExecution : SchemataJobExecution
{
    public async ValueTask<bool> IsCancelledAsync(Guid uid, MessageContext message, CancellationToken ct = default) {
        var scope = await scopes.CreateAsync(message, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(message, ct);
            var repository = scope.Services.GetRequiredService<IRepository<TExecution>>();
            var execution = await repository.FirstOrDefaultAsync(query => query.Where(row => row.Uid == uid), ct);
            return execution?.State is ExecutionState.Cancelled;
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
    }
}

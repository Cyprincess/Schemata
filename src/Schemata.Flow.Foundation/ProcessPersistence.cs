using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Persists Flow process instances and transition history.</summary>
internal sealed class ProcessPersistence
{
    /// <summary>Finds a persisted process by canonical name.</summary>
    public async ValueTask<SchemataProcess?> FindAsync(
        IServiceProvider  services,
        string            canonicalName,
        CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();

        return await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == canonicalName), ct);
    }

    /// <summary>Lists persisted processes that are waiting at a BPMN element.</summary>
    public async IAsyncEnumerable<SchemataProcess> ListWaitingAsync(
        IServiceProvider                           services,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();

        await foreach (var process in processes.ListAsync<SchemataProcess>(
                           q => q.Where(p => p.WaitingAtId != null), ct)) {
            yield return process;
        }
    }

    /// <summary>Stores the current process state and appends its transition history entry in one unit of work.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="process">Current process row.</param>
    /// <param name="transition">Current transition.</param>
    /// <param name="writeback">
    ///     An optional callback enlisted in the transition's unit of work before the process row is
    ///     persisted, projecting the transition onto its source business entity. Running it first lets
    ///     the refreshed <see cref="ISourceReference.SourceTimestamp" /> land in the same write; a
    ///     throw aborts the whole transition.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    public async Task PersistTransitionAsync(
        IServiceProvider                            services,
        SchemataProcess                             process,
        SchemataProcessTransition                   transition,
        Func<IUnitOfWork, CancellationToken, Task>? writeback,
        CancellationToken                           ct
    ) {
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            throw new InvalidOperationException("Process canonical name is required before persistence.");
        }

        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
        var transitions = services.GetRequiredService<IRepository<SchemataProcessTransition>>();

        await using var uow = processes.Begin();
        transitions.Join(uow);

        try {
            if (writeback is not null) {
                await writeback(uow, ct);
            }

            var existing = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);

            if (existing is null) {
                await processes.AddAsync(process, ct);
            } else {
                SyncProcessFields(existing, process);
                await processes.UpdateAsync(existing, ct);
            }

            await transitions.AddAsync(transition, ct);
            await uow.CommitAsync(ct);
        } catch {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Copies persisted process fields from <paramref name="source"/> into <paramref name="target"/>.</summary>
    public static void SyncProcessFields(SchemataProcess target, SchemataProcess source) {
        target.Uid            = source.Uid;
        target.Name           = source.Name;
        target.CanonicalName  = source.CanonicalName;
        target.DefinitionName = source.DefinitionName;
        target.Variables      = source.Variables;
        target.StateId        = source.StateId;
        target.State          = source.State;
        target.WaitingAtId    = source.WaitingAtId;
        target.WaitingAt      = source.WaitingAt;
        target.SourceType    = source.SourceType;
        target.Source = source.Source;
        target.SourceTimestamp     = source.SourceTimestamp;
        target.DisplayName    = source.DisplayName;
        target.DisplayNames   = source.DisplayNames;
        target.Description    = source.Description;
        target.Descriptions   = source.Descriptions;
        target.Timestamp      = source.Timestamp;
        target.CreateTime     = source.CreateTime;
        target.UpdateTime     = source.UpdateTime;
        target.DeleteTime     = source.DeleteTime;
        target.PurgeTime      = source.PurgeTime;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

internal sealed class ProcessPersistence
{
    public async ValueTask<SchemataProcess?> FindAsync(
        IServiceProvider  services,
        string            canonicalName,
        CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();

        return await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == canonicalName), ct);
    }

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

    public async Task PersistTransitionAsync(
        IServiceProvider          services,
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct
    ) {
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            throw new InvalidOperationException("Process canonical name is required before persistence.");
        }

        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
        var transitions = services.GetRequiredService<IRepository<SchemataProcessTransition>>();

        await using var uow = processes.Begin();
        transitions.Join(uow);

        try {
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

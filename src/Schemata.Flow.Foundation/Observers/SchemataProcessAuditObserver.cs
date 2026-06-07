using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation.Observers;

/// <summary>
///     Persists <see cref="SchemataProcess" /> and
///     <see cref="SchemataProcessTransition" /> audit rows in response to
///     <see cref="ProcessRuntime" /> lifecycle events.
/// </summary>
public sealed class SchemataProcessAuditObserver(
    IRepository<SchemataProcess>           processes,
    IRepository<SchemataProcessTransition> transitions
) : IProcessLifecycleObserver
{
    #region IProcessLifecycleObserver Members

    public async Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            return;
        }

        var existing = await processes.FirstOrDefaultAsync(
                           q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
        if (existing is null) {
            await processes.AddAsync(process, ct);
        } else {
            SyncProcessFields(existing, process);
            await processes.UpdateAsync(existing, ct);
        }

        await processes.CommitAsync(ct);
    }

    public async Task OnTransitionedAsync(
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct = default
    ) {
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            return;
        }

        var existing = await processes.FirstOrDefaultAsync(
                           q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
        if (existing is not null) {
            SyncProcessFields(existing, process);
            await processes.UpdateAsync(existing, ct);
        }

        await transitions.AddAsync(transition, ct);
        await transitions.CommitAsync(ct);

        if (existing is not null) {
            await processes.CommitAsync(ct);
        }
    }

    public async Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            return;
        }

        var existing = await processes.FirstOrDefaultAsync(
                           q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
        if (existing is null) {
            return;
        }

        SyncProcessFields(existing, process);
        await processes.UpdateAsync(existing, ct);
        await processes.CommitAsync(ct);
    }

    #endregion

    private static void SyncProcessFields(SchemataProcess target, SchemataProcess source) {
        target.DefinitionName = source.DefinitionName;
        target.Variables      = source.Variables;
        target.StateId        = source.StateId;
        target.State          = source.State;
        target.WaitingAtId    = source.WaitingAtId;
        target.WaitingAt      = source.WaitingAt;
        target.DisplayName    = source.DisplayName;
        target.Description    = source.Description;
    }
}

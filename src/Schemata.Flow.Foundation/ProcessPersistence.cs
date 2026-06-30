using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Coordinates Flow repository work under a shared unit of work.</summary>
public sealed class ProcessPersistence
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

    /// <summary>Lists persisted processes that currently have at least one waiting token.</summary>
    public async IAsyncEnumerable<SchemataProcess> ListWaitingAsync(
        IServiceProvider                           services,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
        var tokens    = services.GetRequiredService<IRepository<SchemataProcessToken>>();

        var waitingProcesses = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var token in tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.WaitingAtName != null), ct)) {
            waitingProcesses.Add(token.Process);
        }

        foreach (var processName in waitingProcesses) {
            var match = await processes.FirstOrDefaultAsync(q => q.Where(p => p.Name == processName), ct);
            if (match is not null) {
                yield return match;
            }
        }
    }

    /// <summary>Runs Flow work with process, token, transition, and source repositories joined.</summary>
    public async Task ExecuteAsync(
        IServiceProvider                                      services,
        Func<FlowPersistenceScope, CancellationToken, Task> work,
        CancellationToken                                     ct
    ) {
        var processes   = services.GetRequiredService<IRepository<SchemataProcess>>();
        var tokens      = services.GetRequiredService<IRepository<SchemataProcessToken>>();
        var transitions = services.GetRequiredService<IRepository<SchemataProcessTransition>>();
        var sources     = services.GetRequiredService<IRepository<SchemataProcessSource>>();

        await using var uow = processes.Begin();
        tokens.Join(uow);
        transitions.Join(uow);
        sources.Join(uow);

        var scope = new FlowPersistenceScope(uow, processes, tokens, transitions, sources);
        try {
            await work(scope, ct);
            await uow.CommitAsync(ct);
        } catch {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Persists the process, token, and transition rows in a runtime snapshot.</summary>
    public async Task PersistSnapshotAsync(FlowPersistenceScope scope, ProcessSnapshot snapshot, CancellationToken ct) {
        var process = snapshot.Process;
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            throw new InvalidOperationException("Process canonical name is required before persistence.");
        }

        var existing = await scope.Processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
        if (existing is null) {
            await scope.Processes.AddAsync(process, ct);
        } else {
            CopyEntity(existing, process);
            await scope.Processes.UpdateAsync(existing, ct);
        }

        foreach (var token in snapshot.Tokens) {
            var persisted = await scope.Tokens.FirstOrDefaultAsync(q => q.Where(t => t.CanonicalName == token.CanonicalName), ct);
            if (persisted is null) {
                await scope.Tokens.AddAsync(token, ct);
            } else {
                CopyEntity(persisted, token);
                await scope.Tokens.UpdateAsync(persisted, ct);
            }
        }

        foreach (var transition in snapshot.Transitions) {
            if (transition.Uid == Guid.Empty) {
                transition.Uid = Identifiers.NewUid();
            }

            await scope.Transitions.AddAsync(transition, ct);
        }
    }

    private static void CopyEntity(object target, object source) {
        switch (target, source) {
            case (SchemataProcess dst, SchemataProcess src):
                dst.Uid            = src.Uid;
                dst.Name           = src.Name;
                dst.CanonicalName  = src.CanonicalName;
                dst.DefinitionName = src.DefinitionName;
                dst.State          = src.State;
                dst.DisplayName    = src.DisplayName;
                dst.DisplayNames   = src.DisplayNames;
                dst.Description    = src.Description;
                dst.Descriptions   = src.Descriptions;
                dst.Annotations    = new(src.Annotations);
                dst.Timestamp      = src.Timestamp;
                dst.CreateTime     = src.CreateTime;
                dst.UpdateTime     = src.UpdateTime;
                dst.DeleteTime     = src.DeleteTime;
                dst.PurgeTime      = src.PurgeTime;
                break;
            case (SchemataProcessToken dst, SchemataProcessToken src):
                dst.Uid           = src.Uid;
                dst.Name          = src.Name;
                dst.CanonicalName = src.CanonicalName;
                dst.Process       = src.Process;
                dst.Spawner       = src.Spawner;
                dst.ScopeName     = src.ScopeName;
                dst.StateName     = src.StateName;
                dst.WaitingAtName = src.WaitingAtName;
                dst.Bookkeeping   = new(src.Bookkeeping);
                dst.Annotations   = new(src.Annotations);
                dst.State         = src.State;
                dst.Timestamp     = src.Timestamp;
                dst.CreateTime    = src.CreateTime;
                dst.UpdateTime    = src.UpdateTime;
                dst.DeleteTime    = src.DeleteTime;
                dst.PurgeTime     = src.PurgeTime;
                break;
        }
    }
}

/// <summary>Joined repositories and unit of work for a Flow operation.</summary>
public sealed class FlowPersistenceScope(
    IUnitOfWork                             unitOfWork,
    IRepository<SchemataProcess>           processes,
    IRepository<SchemataProcessToken>      tokens,
    IRepository<SchemataProcessTransition> transitions,
    IRepository<SchemataProcessSource>     sources
)
{
    /// <summary>The unit of work shared by all repositories.</summary>
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IRepository<SchemataProcess> Processes { get; } = processes;

    public IRepository<SchemataProcessToken> Tokens { get; } = tokens;

    public IRepository<SchemataProcessTransition> Transitions { get; } = transitions;

    public IRepository<SchemataProcessSource> Sources { get; } = sources;
}

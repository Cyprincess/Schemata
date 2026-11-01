using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Coordinates Flow repository work under a shared unit of work.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
/// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
/// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
public sealed class ProcessPersistence<TProcess, TToken, TTransition, TCompensation, TSource>
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
    where TCompensation : SchemataProcessCompensation, new()
    where TSource : SchemataProcessSource, new()
{
    /// <summary>Finds a persisted process by canonical name.</summary>
    public async ValueTask<TProcess?> FindAsync(
        IServiceProvider  services,
        string            canonicalName,
        CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<TProcess>>();
        return await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == canonicalName), ct);
    }

    /// <summary>Lists persisted processes that currently have at least one waiting token.</summary>
    public async IAsyncEnumerable<TProcess> ListWaitingAsync(
        IServiceProvider                           services,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<TProcess>>();
        var tokens    = services.GetRequiredService<IRepository<TToken>>();

        var waitingProcesses = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var token in tokens.ListAsync<TToken>(q => q.Where(t => t.WaitingAtName != null), ct)) {
            waitingProcesses.Add(token.Process);
        }

        foreach (var processName in waitingProcesses) {
            var match = await processes.FirstOrDefaultAsync(q => q.Where(p => p.Name == processName), ct);
            if (match is not null) {
                yield return match;
            }
        }
    }
    /// <summary>Lists persisted processes that currently have at least one active token not waiting at an element.</summary>
    public async IAsyncEnumerable<TProcess> ListActiveHostsAsync(
        IServiceProvider                           services,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<TProcess>>();
        var tokens    = services.GetRequiredService<IRepository<TToken>>();

        var activeProcesses = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var token in tokens.ListAsync<TToken>(
                           q => q.Where(t => t.WaitingAtName == null && t.State == "Active"), ct)) {
            activeProcesses.Add(token.Process);
        }

        foreach (var processName in activeProcesses) {
            var match = await processes.FirstOrDefaultAsync(q => q.Where(p => p.Name == processName), ct);
            if (match is not null) {
                yield return match;
            }
        }
    }


    /// <summary>Runs Flow work with process, token, transition, and source repositories joined.</summary>
    public async Task ExecuteAsync(
        IServiceProvider                                                         services,
        Func<FlowPersistenceScope<TProcess, TToken, TTransition, TCompensation, TSource>, CancellationToken, Task> work,
        CancellationToken                                                        ct
    ) {
        var processes     = services.GetRequiredService<IRepository<TProcess>>();
        var tokens        = services.GetRequiredService<IRepository<TToken>>();
        var transitions   = services.GetRequiredService<IRepository<TTransition>>();
        var sources       = services.GetRequiredService<IRepository<TSource>>();
        var compensations = services.GetRequiredService<IRepository<TCompensation>>();

        await using var uow = processes.Begin();
        tokens.Join(uow);
        transitions.Join(uow);
        sources.Join(uow);
        compensations.Join(uow);

        var scope = new FlowPersistenceScope<TProcess, TToken, TTransition, TCompensation, TSource>(
            uow, processes, tokens, transitions, sources, compensations, services);
        try {
            await work(scope, ct);
            await uow.CommitAsync(ct);
        } catch {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Persists the process, token, and transition rows in a runtime snapshot.</summary>
    public async Task PersistSnapshotAsync(
        FlowPersistenceScope<TProcess, TToken, TTransition, TCompensation, TSource> scope,
        ProcessSnapshot                                                             snapshot,
        CancellationToken                                                           ct
    ) {
        var process = snapshot.Process;
        if (string.IsNullOrWhiteSpace(process.CanonicalName)) {
            throw new InvalidOperationException("Process canonical name is required before persistence.");
        }

        ReleaseIdempotencyKey(process);
        var typedProcess = (TProcess)process;

        if (scope.CreatedProcesses.Contains(typedProcess)) {
            await scope.Mutation<TProcess>().UpdateAsync(typedProcess, scope.UnitOfWork, ct: ct);
        } else {
            var existing = await scope.Processes.FirstOrDefaultAsync(
                q => q.Where(p => p.CanonicalName == process.CanonicalName), ct,
                new QueryOptions(IgnoreOwner: true));
            if (existing is null) {
                await scope.CreateProcessAsync(typedProcess, ct);
            } else {
                if (!ReferenceEquals(existing, process)) CopyEntity(existing, process);
                await scope.Mutation<TProcess>().UpdateAsync(existing, scope.UnitOfWork, ct: ct);
                // The rotated stamp is final only at the provider's write boundary; the preparation
                // projects it onto the runtime process after rotation, and rollback restores the
                // staged value so a failed commit never surfaces an unpersisted stamp.
                var staged = process.Timestamp;
                scope.UnitOfWork.AddSavePreparation(() => process.Timestamp = existing.Timestamp);
                scope.UnitOfWork.AddRollbackSink(() => process.Timestamp = staged);
                process.UpdateTime = existing.UpdateTime;
            }
        }

        Dictionary<string, TToken>? tokenIndex = null;
        foreach (var token in snapshot.Tokens) {
            var typedToken = (TToken)token;
            if (scope.CreatedTokens.Contains(typedToken)) {
                await scope.Mutation<TToken>().UpdateAsync(typedToken, scope.UnitOfWork, ct: ct);
                continue;
            }
            tokenIndex ??= await scope.GetTokenIndexAsync(process.Name!, ct);
            var persisted = token.CanonicalName is { } name && tokenIndex.TryGetValue(name, out var loaded) ? loaded : null;
            if (persisted is null) {
                await scope.CreateTokenAsync(typedToken, ct);
            } else {
                if (!ReferenceEquals(persisted, token)) CopyEntity(persisted, token);
                await scope.Mutation<TToken>().UpdateAsync(persisted, scope.UnitOfWork, ct: ct);
                var staged = token.Timestamp;
                scope.UnitOfWork.AddSavePreparation(() => token.Timestamp = persisted.Timestamp);
                scope.UnitOfWork.AddRollbackSink(() => token.Timestamp = staged);
                token.UpdateTime = persisted.UpdateTime;
            }
        }

        foreach (var transition in snapshot.Transitions) {
            await scope.Mutation<TTransition>().CreateAsync((TTransition)transition, scope.UnitOfWork, ct);
        }

        await ReplaceCompensationBindingsAsync(scope, process, snapshot.CompensationBindings, ct);
    }

    private static async Task ReplaceCompensationBindingsAsync(
        FlowPersistenceScope<TProcess, TToken, TTransition, TCompensation, TSource> scope,
        SchemataProcess                                                            process,
        IReadOnlyList<ProcessCompensationBinding>                                  bindings,
        CancellationToken                                                         ct
    ) {
        var remaining = new Dictionary<(string Scope, string Activity, string Boundary, int Order), int>();
        if (!ProcessStates.IsTerminal(process.State)) {
            foreach (var binding in bindings) {
                var key = (binding.ScopeOwnerCanonicalName, binding.ActivityName, binding.BoundaryName, binding.RegistrationOrder);
                remaining.TryGetValue(key, out var count);
                remaining[key] = count + 1;
            }
        }
        var removed = new List<TCompensation>();
        var effective = await scope.GetCompensationsAsync(process.CanonicalName!, ct);
        foreach (var row in effective) {
            var key = (row.ScopeOwnerCanonicalName, row.ActivityName, row.BoundaryName, row.RegistrationOrder);
            if (remaining.TryGetValue(key, out var count) && count > 0) remaining[key] = count - 1;
            else removed.Add(row);
        }
        if (removed.Count > 0) {
            var mutation = scope.Mutation<TCompensation>();
            foreach (var row in removed) {
                await mutation.DeleteAsync(row, scope.UnitOfWork, ct: ct);
            }

            var removedSet = new HashSet<TCompensation>(removed, ReferenceEqualityComparer.Instance);
            effective.RemoveAll(removedSet.Contains);
        }
        var added = new List<TCompensation>();
        foreach (var (key, count) in remaining) {
            for (var i = 0; i < count; i++) {
                added.Add(new() {
                    Process = process.CanonicalName!, ScopeOwnerCanonicalName = key.Scope,
                    ActivityName = key.Activity, BoundaryName = key.Boundary, RegistrationOrder = key.Order,
                });
            }
        }
        if (added.Count > 0) {
            var mutation = scope.Mutation<TCompensation>();
            foreach (var row in added) {
                await mutation.CreateAsync(row, scope.UnitOfWork, ct);
            }

            effective.AddRange(added);
        }
    }

    private static void CopyEntity(object target, object source) {
        switch (target, source) {
            case (TProcess dst, TProcess src):
                dst.Uid            = src.Uid;
                dst.Name           = src.Name;
                dst.CanonicalName  = src.CanonicalName;
                dst.DefinitionName = src.DefinitionName;
                dst.DefinitionVersion = src.DefinitionVersion;
                dst.IdempotencyKey = src.IdempotencyKey;
                dst.State          = src.State;
                dst.Annotations    = new(src.Annotations);
                dst.Timestamp      = src.Timestamp;
                dst.CreateTime     = src.CreateTime;
                dst.UpdateTime     = src.UpdateTime;
                dst.DeleteTime     = src.DeleteTime;
                dst.PurgeTime      = src.PurgeTime;
                src.CopyLabels(dst);
                break;
            case (TToken dst, TToken src):
                dst.Uid                   = src.Uid;
                dst.Name                  = src.Name;
                dst.CanonicalName         = src.CanonicalName;
                dst.Process               = src.Process;
                dst.Spawner               = src.Spawner;
                dst.ScopeName             = src.ScopeName;
                dst.StateName             = src.StateName;
                dst.WaitingAtName         = src.WaitingAtName;
                dst.CurrentCallActivation = src.CurrentCallActivation;
                dst.Bookkeeping           = new(src.Bookkeeping);
                dst.Annotations           = new(src.Annotations);
                dst.State                 = src.State;
                dst.Timestamp             = src.Timestamp;
                dst.CreateTime            = src.CreateTime;
                dst.UpdateTime            = src.UpdateTime;
                dst.DeleteTime            = src.DeleteTime;
                dst.PurgeTime             = src.PurgeTime;
                break;
        }
    }

    private static void ReleaseIdempotencyKey(SchemataProcess process) {
        if (!ProcessStates.IsTerminal(process.State) || string.IsNullOrEmpty(process.IdempotencyKey)) {
            return;
        }

        process.Annotations["schemata/flow/idempotency-key"] = process.IdempotencyKey;
        process.IdempotencyKey = null;
    }

}

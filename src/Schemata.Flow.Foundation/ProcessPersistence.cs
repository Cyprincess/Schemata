using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

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
        var tenant = TenantContext.Current.Uid;
        return await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == canonicalName && p.TenantUid == tenant), ct);
    }

    /// <summary>Lists persisted processes that currently have at least one waiting token.</summary>
    public async IAsyncEnumerable<SchemataProcess> ListWaitingAsync(
        IServiceProvider                           services,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
        var tokens    = services.GetRequiredService<IRepository<SchemataProcessToken>>();
        var tenant = TenantContext.Current.Uid;

        var waitingProcesses = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var token in tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.WaitingAtName != null && t.TenantUid == tenant), ct)) {
            waitingProcesses.Add(token.Process);
        }

        foreach (var processName in waitingProcesses) {
            var match = await processes.FirstOrDefaultAsync(q => q.Where(p => p.Name == processName && p.TenantUid == tenant), ct);
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
        var processes     = services.GetRequiredService<IRepository<SchemataProcess>>();
        var tokens        = services.GetRequiredService<IRepository<SchemataProcessToken>>();
        var transitions   = services.GetRequiredService<IRepository<SchemataProcessTransition>>();
        var sources       = services.GetRequiredService<IRepository<SchemataProcessSource>>();
        var compensations = services.GetRequiredService<IRepository<SchemataProcessCompensation>>();

        await using var uow = processes.Begin();
        tokens.Join(uow);
        transitions.Join(uow);
        sources.Join(uow);
        compensations.Join(uow);

        var scope = new FlowPersistenceScope(uow, processes, tokens, transitions, sources, compensations, services);
        // The suppression scope covers the work and the commit; disposal restores the prior state.
        using var suppression = SuppressOwnerQueries(processes, tokens, transitions, sources, compensations);
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

        ReleaseIdempotencyKey(process);

        if (scope.CreatedProcesses.Contains(process)) {
            await scope.Mutation<SchemataProcess>().UpdateAsync(process, scope.UnitOfWork, ct: ct);
        } else {
            var existing = await scope.Processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
            if (existing is null) {
                await scope.CreateProcessAsync(process, ct);
            } else {
                if (!ReferenceEquals(existing, process)) CopyEntity(existing, process);
                await scope.Mutation<SchemataProcess>().UpdateAsync(existing, scope.UnitOfWork, ct: ct);
                // The rotated stamp is final only at the provider's write boundary; the preparation
                // projects it onto the runtime process after rotation, and rollback restores the
                // staged value so a failed commit never surfaces an unpersisted stamp.
                var staged = process.Timestamp;
                scope.UnitOfWork.AddSavePreparation(() => process.Timestamp = existing.Timestamp);
                scope.UnitOfWork.AddRollbackSink(() => process.Timestamp = staged);
                process.UpdateTime = existing.UpdateTime;
            }
        }

        Dictionary<string, SchemataProcessToken>? tokenIndex = null;
        foreach (var token in snapshot.Tokens) {
            token.TenantUid = process.TenantUid;
            if (scope.CreatedTokens.Contains(token)) {
                await scope.Mutation<SchemataProcessToken>().UpdateAsync(token, scope.UnitOfWork, ct: ct);
                continue;
            }
            tokenIndex ??= await scope.GetTokenIndexAsync(process.Name!, ct);
            var persisted = token.CanonicalName is { } name && tokenIndex.TryGetValue(name, out var loaded) ? loaded : null;
            if (persisted is null) {
                await scope.CreateTokenAsync(token, ct);
            } else {
                if (!ReferenceEquals(persisted, token)) CopyEntity(persisted, token);
                await scope.Mutation<SchemataProcessToken>().UpdateAsync(persisted, scope.UnitOfWork, ct: ct);
                var staged = token.Timestamp;
                scope.UnitOfWork.AddSavePreparation(() => token.Timestamp = persisted.Timestamp);
                scope.UnitOfWork.AddRollbackSink(() => token.Timestamp = staged);
                token.UpdateTime = persisted.UpdateTime;
            }
        }

        foreach (var transition in snapshot.Transitions) {
            transition.TenantUid = process.TenantUid;
            await scope.Mutation<SchemataProcessTransition>().CreateAsync(transition, scope.UnitOfWork, ct);
        }

        await ReplaceCompensationBindingsAsync(scope, process, snapshot.CompensationBindings, ct);
    }

    private static async Task ReplaceCompensationBindingsAsync(
        FlowPersistenceScope                     scope,
        SchemataProcess                          process,
        IReadOnlyList<ProcessCompensationBinding> bindings,
        CancellationToken                        ct
    ) {
        var remaining = new Dictionary<(string Scope, string Activity, int Order), int>();
        if (!ProcessStates.IsTerminal(process.State)) {
            foreach (var binding in bindings) {
                var key = (binding.ScopeOwnerCanonicalName, binding.ActivityName, binding.RegistrationOrder);
                remaining.TryGetValue(key, out var count);
                remaining[key] = count + 1;
            }
        }
        var removed = new List<SchemataProcessCompensation>();
        var effective = await scope.GetCompensationsAsync(process.CanonicalName!, ct);
        foreach (var row in effective) {
            var key = (row.ScopeOwnerCanonicalName, row.ActivityName, row.RegistrationOrder);
            if (remaining.TryGetValue(key, out var count) && count > 0) remaining[key] = count - 1;
            else removed.Add(row);
        }
        if (removed.Count > 0) {
            var mutation = scope.Mutation<SchemataProcessCompensation>();
            foreach (var row in removed) {
                await mutation.DeleteAsync(row, scope.UnitOfWork, ct: ct);
            }

            var removedSet = new HashSet<SchemataProcessCompensation>(removed, ReferenceEqualityComparer.Instance);
            effective.RemoveAll(removedSet.Contains);
        }
        var added = new List<SchemataProcessCompensation>();
        foreach (var (key, count) in remaining) {
            for (var i = 0; i < count; i++) {
                added.Add(new() {
                    Process = process.CanonicalName!, ScopeOwnerCanonicalName = key.Scope,
                    ActivityName = key.Activity, RegistrationOrder = key.Order,
                });
            }
        }
        if (added.Count > 0) {
            var mutation = scope.Mutation<SchemataProcessCompensation>();
            foreach (var row in added) {
                await mutation.CreateAsync(row, scope.UnitOfWork, ct);
            }

            effective.AddRange(added);
        }
    }

    private static void CopyEntity(object target, object source) {
        switch (target, source) {
            case (SchemataProcess dst, SchemataProcess src):
                dst.Uid            = src.Uid;
                dst.Name           = src.Name;
                dst.CanonicalName  = src.CanonicalName;
                dst.DefinitionName = src.DefinitionName;
                dst.DefinitionVersion = src.DefinitionVersion;
                dst.TenantUid = src.TenantUid;
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
            case (SchemataProcessToken dst, SchemataProcessToken src):
                dst.Uid           = src.Uid;
                dst.Name          = src.Name;
                dst.CanonicalName = src.CanonicalName;
                dst.Process       = src.Process;
                dst.TenantUid = src.TenantUid;
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

    private static void ReleaseIdempotencyKey(SchemataProcess process) {
        if (!ProcessStates.IsTerminal(process.State) || string.IsNullOrEmpty(process.IdempotencyKey)) {
            return;
        }

        process.Annotations["schemata/flow/idempotency-key"] = process.IdempotencyKey;
        process.IdempotencyKey = null;
    }

    private static IDisposable SuppressOwnerQueries(params IRepository[] repositories) {
        return new SuppressionScope(repositories.Select(repository => repository.AdviceContext.Use<QueryOwnerSuppressed>()).ToArray());
    }

    private sealed class SuppressionScope(IDisposable[] scopes) : IDisposable
    {
        private bool _disposed;

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            for (var i = scopes.Length - 1; i >= 0; i--) {
                scopes[i].Dispose();
            }
        }
    }
}
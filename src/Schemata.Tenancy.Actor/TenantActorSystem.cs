using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Actor;

/// <summary>
///     Routes actor operations to a per-tenant <see cref="InProcessActorSystem{TActor}" /> owner. A
///     retained <see cref="IActorRef" /> never pins the runtime it was created against: every
///     turn re-resolves the current owner for its tenant identity, so a runtime retired by idle
///     collection is only ever replaced by a new owner, never resurrected through a stale
///     reference. The single idle collector also retires runtimes whose activations have all
///     finished, and retirement is mutually exclusive with turn admission through in-flight
///     leases, so a runtime removed from the owner table cannot admit another turn.
/// </summary>
/// <typeparam name="TActor">The persisted actor-state entity type every per-tenant runtime is closed on.</typeparam>
public sealed class TenantActorSystem<TActor>(
    IServiceProvider services, IActorRegistry registry, IMessageExecutionScopeFactory scopes,
    IOptions<SchemataActorOptions> options, TimeProvider? clock = null) : IActorSystem, IActorSystemLifetime
    where TActor : SchemataActor, new()
{
    private readonly object _gate = new();
    private readonly Dictionary<TenantIdentity, RuntimeOwner> _owners = [];
    private bool _stopping;
    private Task? _shutdown;

    /// <summary>
    ///     Runs <paramref name="action" /> against the current owner runtime for the ambient
    ///     tenant, holding an in-flight lease for the whole operation. Runtime retirement waits
    ///     for zero leases, so the collector can never close a runtime mid-turn.
    /// </summary>
    private async Task<T> WithRuntimeAsync<T>(Func<InProcessActorSystem<TActor>, Task<T>> action) {
        RuntimeOwner owner;
        lock (_gate) {
            if (_stopping) throw new InvalidOperationException("The actor system is stopping.");
            var identity = TenantContext.Current;
            if (!_owners.TryGetValue(identity, out owner!)) {
                owner = new RuntimeOwner(new(services, registry, scopes, options, clock,
                    (id, props, context) => new Reference(this, identity, id, props, context, scopes),
                    () => CaptureAsync(identity)));
                _owners.Add(identity, owner);
            }
            owner.Leases++;
        }
        try { return await action(owner.Runtime); }
        finally {
            lock (_gate) owner.Leases--;
        }
    }

    private async Task WithRuntimeAsync(Func<InProcessActorSystem<TActor>, Task> action) {
        await WithRuntimeAsync<object?>(async runtime => {
            await action(runtime);
            return null;
        });
    }

    public Task<IActorRef> SpawnAsync(ActorId id, Props props) => WithRuntimeAsync(runtime => runtime.SpawnAsync(id, props));

    public Task<IActorRef> GetAsync(ActorId id) => WithRuntimeAsync(runtime => runtime.GetAsync(id));

    private async ValueTask<MessageContext> CaptureAsync(TenantIdentity identity) {
        using var entered = TenantContext.Enter(identity);
        return await MessageContexts.CaptureAsync(services, scopes: scopes);
    }

    public Task StopAsync(ActorId id) => WithRuntimeAsync(async runtime => {
        var context = await CaptureAsync(TenantContext.Current);
        var scope = await scopes.CreateAsync(context);
        IDisposable? lease = null;
        Exception? failure = null;
        try {
            lease = scope.Enter();
            await scope.RestoreAsync(context);
            await runtime.StopAsync(id);
        } catch (Exception error) { failure = error; }
        try { await scope.DisposeAsync(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        try { lease?.Dispose(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    });

    public Task BeginShutdown() {
        lock (_gate) {
            _stopping = true;
            return _shutdown ??= Task.WhenAll(_owners.Values.Select(owner => owner.Runtime.BeginShutdown()));
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken) {
        var shutdown = BeginShutdown();
        InProcessActorSystem<TActor>[] runtimes;
        lock (_gate) runtimes = _owners.Values.Select(owner => owner.Runtime).ToArray();
        await Task.WhenAll(shutdown.WaitAsync(cancellationToken),
            Task.WhenAll(runtimes.Select(runtime => runtime.ShutdownAsync(cancellationToken))));
    }

    public async Task CollectIdleAsync(CancellationToken cancellationToken) {
        using var timer = new PeriodicTimer(options.Value.IdleScanInterval, clock ?? TimeProvider.System);
        while (await timer.WaitForNextTickAsync(cancellationToken)) {
            KeyValuePair<TenantIdentity, RuntimeOwner>[] owners;
            lock (_gate) {
                if (_stopping) return;
                owners = _owners.ToArray();
            }
            await Task.WhenAll(owners.Select(owner => owner.Value.Runtime.CollectIdle()));
            List<InProcessActorSystem<TActor>>? retired = null;
            lock (_gate) {
                if (_stopping) return;
                foreach (var owner in owners) {
                    if (!ReferenceEquals(_owners.GetValueOrDefault(owner.Key), owner.Value)) continue;
                    if (owner.Value.Leases != 0) continue;
                    if (!owner.Value.Runtime.TryCloseIfIdle()) continue;
                    _owners.Remove(owner.Key);
                    (retired ??= []).Add(owner.Value.Runtime);
                }
            }
            if (retired is null) continue;
            await Task.WhenAll(retired.Select(runtime => runtime.ShutdownAsync(cancellationToken)));
        }
    }

    public async ValueTask DisposeAsync() {
        await ShutdownAsync(CancellationToken.None);
    }

    private sealed class RuntimeOwner(InProcessActorSystem<TActor> runtime)
    {
        public InProcessActorSystem<TActor> Runtime { get; } = runtime;

        /// <summary>In-flight operations resolved through this owner; every access holds <c>TenantActorSystem&lt;TActor&gt;._gate</c>.</summary>
        public int Leases { get; set; }
    }

    /// <summary>
    ///     A reference that resolves the tenant's current owner runtime on every turn. The
    ///     mailbox-level reference exists only for the duration of one turn, so retiring a
    ///     runtime never strands the reference: the next turn resolves the same tenant identity
    ///     to a new owner and reactivates there.
    /// </summary>
    private sealed class Reference(TenantActorSystem<TActor> system, TenantIdentity identity, ActorId id, Props props,
        MessageContext captured, IMessageExecutionScopeFactory scopes) : IActorRef
    {
        public ActorId Id => id;

        private void Validate() {
            if (TenantContext.Current != identity) throw new InvalidOperationException("Actor reference belongs to a different tenant identity.");
        }

        public ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default) where T : IMessage {
            Validate();
            return new ValueTask(system.WithRuntimeAsync(async runtime => {
                var inner = runtime.CreateReference(id, props, captured);
                var scope = await scopes.CreateAsync(context ?? captured, ct);
                IDisposable? lease = null;
                Exception? failure = null;
                try {
                    lease = scope.Enter();
                    Validate();
                    await scope.RestoreAsync(context ?? captured, ct);
                    await inner.TellAsync(message, context ?? captured, ct);
                } catch (Exception error) { failure = error; }
                try { await scope.DisposeAsync(); }
                catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
                try { lease?.Dispose(); }
                catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            }));
        }

        public ValueTask<TResponse> AskAsync<TRequest, TResponse>(TRequest request, MessageContext? context = null,
            TimeSpan? timeout = null, CancellationToken ct = default) where TRequest : IRequest<TResponse> {
            Validate();
            return new ValueTask<TResponse>(system.WithRuntimeAsync(async runtime => {
                var inner = runtime.CreateReference(id, props, captured);
                var scope = await scopes.CreateAsync(context ?? captured, ct);
                IDisposable? lease = null;
                Exception? failure = null;
                TResponse response = default!; // definite-assignment sentinel: assigned on every path before the result is used
                try {
                    lease = scope.Enter();
                    Validate();
                    await scope.RestoreAsync(context ?? captured, ct);
                    response = await inner.AskAsync<TRequest, TResponse>(request, context ?? captured, timeout, ct);
                } catch (Exception error) { failure = error; }
                try { await scope.DisposeAsync(); }
                catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
                try { lease?.Dispose(); }
                catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                return response;
            }));
        }
    }
}

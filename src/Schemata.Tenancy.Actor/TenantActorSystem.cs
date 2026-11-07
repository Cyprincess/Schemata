using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Actor;

/// <summary>
///     Owns one actor runtime per explicit tenant identity. Retained references resolve the current
///     runtime for every operation, allowing idle runtimes to retire without stranding references.
/// </summary>
/// <typeparam name="TActor">The persisted actor-state entity type every per-tenant runtime is closed on.</typeparam>
public sealed class TenantActorSystem<TActor>(
    IServiceProvider services, IActorRegistry registry, IMessageExecutionScopeFactory scopes,
    IOptions<SchemataActorOptions> options, TimeProvider? clock = null) : IActorSystemLifetime
    where TActor : SchemataActor, new()
{
    private readonly object _gate = new();
    private readonly IMessageExecutionScopeRunner _runner = new MessageExecutionScopeRunner(scopes);
    private readonly Dictionary<TenantIdentity, RuntimeOwner> _owners = [];
    private bool _stopping;
    private Task? _shutdown;

    private async Task<T> WithRuntimeAsync<T>(TenantIdentity identity, Func<InProcessActorSystem<TActor>, Task<T>> action) {
        RuntimeOwner owner;
        lock (_gate) {
            if (_stopping) throw new InvalidOperationException("The actor system is stopping.");
            if (!_owners.TryGetValue(identity, out var selected)) {
                selected = new RuntimeOwner(new(services, registry, scopes, options, clock,
                    (id, props, context) => new Reference(this, identity, id, props, context,
                        _runner),
                    () => CaptureAsync(identity)));
                _owners.Add(identity, selected);
            }
            owner = selected;
            owner.Leases++;
        }
        try {
            return await action(owner.Runtime);
        } finally {
            lock (_gate) owner.Leases--;
        }
    }

    private async Task WithRuntimeAsync(TenantIdentity identity, Func<InProcessActorSystem<TActor>, Task> action) {
        await WithRuntimeAsync<object?>(identity, async runtime => {
            await action(runtime);
            return null;
        });
    }

    public Task<IActorRef> SpawnAsync(TenantIdentity identity, IServiceProvider invocation, ActorId id, Props props) {
        Validate(identity, invocation);
        return WithRuntimeAsync(identity, runtime => runtime.SpawnAsync(id, props));
    }

    public Task<IActorRef> GetAsync(TenantIdentity identity, IServiceProvider invocation, ActorId id) {
        Validate(identity, invocation);
        return WithRuntimeAsync(identity, runtime => runtime.GetAsync(id));
    }

    private static void Validate(TenantIdentity identity, IServiceProvider invocation) {
        if (invocation.GetRequiredService<TenantIdentity>() != identity) {
            throw new InvalidOperationException("The actor facade belongs to a different tenant identity.");
        }
    }

    private async ValueTask<MessageContext> CaptureAsync(TenantIdentity identity) {
        var context = new MessageContext(new Dictionary<string, string?> {
            [TenancyConstants.Messaging.TenantIdKey] = identity.Uid?.ToString("D") ?? "host",
        });
        var runner = _runner;
        return await runner.RunAsync(context, (provider, _) => ValueTask.FromResult(MessageContexts.Capture(provider)));
    }

    public Task StopAsync(TenantIdentity identity, IServiceProvider invocation, ActorId id) => WithRuntimeAsync(identity, async runtime => {
        var context = await CaptureAsync(identity);
        await _runner.RunAsync(context, async (_, token) => {
            await runtime.StopAsync(id);
            return true;
        });
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

    private sealed class Reference(TenantActorSystem<TActor> system, TenantIdentity identity, ActorId id, Props props,
        MessageContext captured, IMessageExecutionScopeRunner runner) : IActorRef
    {
        public ActorId Id => id;

        private void Validate(MessageContext? context) {
            if (context is null || !context.Items.TryGetValue(TenancyConstants.Messaging.TenantIdKey, out var value)
                || (identity.Uid is { } uid ? !Guid.TryParse(value, out var supplied) || supplied != uid : value != "host")) {
                throw new InvalidOperationException("Actor reference requires current message evidence for its tenant identity.");
            }
        }

        public ValueTask TellAsync<T>(IServiceProvider services, T message, MessageContext? context = null, CancellationToken ct = default) where T : IMessage {
            Validate(context);
            return new ValueTask(system.WithRuntimeAsync(identity, async runtime => {
                var inner = runtime.CreateReference(id, props, captured);
                await runner.RunAsync(context, async (provider, token) => {
                    await inner.TellAsync(services, message, context, token);
                    return true;
                }, ct);
            }));
        }

        public ValueTask<TResponse> AskAsync<TRequest, TResponse>(IServiceProvider services, TRequest request, MessageContext? context = null,
            TimeSpan? timeout = null, CancellationToken ct = default) where TRequest : IRequest<TResponse> {
            Validate(context);
            return new ValueTask<TResponse>(system.WithRuntimeAsync(identity, runtime => {
                var inner = runtime.CreateReference(id, props, captured);
                return runner.RunAsync(context, async (provider, token) => {
                    return await inner.AskAsync<TRequest, TResponse>(services, request, context, timeout, token);
                }, ct).AsTask();
            }));
        }
    }
}

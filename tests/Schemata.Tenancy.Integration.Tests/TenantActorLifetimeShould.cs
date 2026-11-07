using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Actor;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantActorLifetimeShould
{
    [Fact]
    public async Task Idle_Collector_Surfaces_Actor_Disposal_Failure_Under_Captured_Identity() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var failure = new InvalidOperationException("idle disposal");
        var stopped = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((_, _) => ValueTask.FromResult(
                Scope(root, tenant)));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions { IdleTimeout = TimeSpan.FromSeconds(1), IdleScanInterval = TimeSpan.FromSeconds(1) }), clock);
        var reference = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "idle-failure"), new(typeof(IdleFailureActor), [failure, stopped]));
        Assert.Equal(tenant.Uid, await reference.AskAsync<Read, Guid?>(root, new(), Context(tenant)));
        using var cancellation = new CancellationTokenSource();
        var collector = system.CollectIdleAsync(cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!collector.IsCompleted) {
            deadline.Token.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromSeconds(2));
            await Task.Yield();
        }
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => collector));
        Assert.Equal(tenant.Uid, await stopped.Task);
        await system.ShutdownAsync(default);
    }

    public sealed class IdleFailureActor(Exception failure, TaskCompletionSource<Guid?> stopped) : IActor, IAsyncDisposable
    {
        private TenantIdentity _identity;
        public ValueTask OnStartedAsync(IActorContext context) {
            _identity = context.Services.GetRequiredService<TenantIdentity>();
            return ValueTask.CompletedTask;
        }
        public ValueTask OnStoppedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(context.Services.GetRequiredService<TenantIdentity>().Uid);
        public async ValueTask DisposeAsync() {
            await Task.Yield();
            stopped.TrySetResult(_identity.Uid);
            throw failure;
        }
    }

    [Fact]
    public async Task Captures_Tenant_Only_Scoped_Propagators_From_Selected_Provider() {
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        var captured = new List<Guid?>();
        var disposed = new List<Guid?>();
        var propagation = new Mock<IMessageContextPropagator>();
        propagation.Setup(value => value.Capture(It.IsAny<IDictionary<string, string?>>(), It.IsAny<IServiceProvider>()))
            .Callback<IDictionary<string, string?>, IServiceProvider>((items, provider) => {
                var identity = provider.GetRequiredService<TenantIdentity>();
                captured.Add(identity.Uid);
                items[TenancyConstants.Messaging.TenantIdKey] = identity.Uid?.ToString("D") ?? "host";
            });
        await using var host = new ServiceCollection().AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>()
            .AddSchemataActor().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var selectedServices = new ServiceCollection();
        selectedServices.AddSchemataActor();
        selectedServices.AddSingleton(disposed);
        selectedServices.AddScoped(_ => propagation.Object);
        selectedServices.AddScoped<LifetimeProbe>();
        await using var selected = selectedServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => {
                var identity = new TenantIdentity(Guid.Parse(context!.Items[TenancyConstants.Messaging.TenantIdKey]!));
                var selectedScope = Scope(selected, identity);
                var probe = selectedScope.Services.GetRequiredService<LifetimeProbe>();
                probe.Identity = identity;
                return ValueTask.FromResult(selectedScope);
            });
        await using var system = new TenantActorSystem<SchemataActor>(host, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        await using var caller = host.CreateAsyncScope();
        var actorA = await system.SpawnAsync(a, Invocation(host, a), new("probe", "same"), new(typeof(ProbeActor), [new Probe()]));
        Assert.Equal(a.Uid, await actorA.AskAsync<Read, Guid?>(caller.ServiceProvider, new(), Context(a)));
        var actorB = await system.SpawnAsync(b, Invocation(host, b), new("probe", "same"), new(typeof(ProbeActor), [new Probe()]));
        Assert.Equal(b.Uid, await actorB.AskAsync<Read, Guid?>(caller.ServiceProvider, new(), Context(b)));
        await system.ShutdownAsync(default);
        Assert.Equal(new Guid?[] { a.Uid, b.Uid }, captured);
        Assert.Contains(a.Uid, disposed);
        Assert.Contains(b.Uid, disposed);
        Assert.DoesNotContain(null, disposed);
    }

    [Fact]
    public async Task Ordinary_Runtime_Captures_Scoped_Propagators_Without_Root_Resolution() {
        var captureProviders = new List<IServiceProvider>();
        var propagation = new Mock<IMessageContextPropagator>();
        propagation.Setup(value => value.Capture(It.IsAny<IDictionary<string, string?>>(), It.IsAny<IServiceProvider>()))
            .Callback<IDictionary<string, string?>, IServiceProvider>((_, provider) => captureProviders.Add(provider));
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddScoped(_ => propagation.Object);
        services.AddSingleton<Schemata.Messaging.Skeleton.IMessageExecutionScopeFactory, Schemata.Messaging.Skeleton.Runtime.MessageExecutionScopeFactory>();
        services.AddSingleton<Schemata.Messaging.Skeleton.IMessageExecutionScopeRunner, Schemata.Messaging.Skeleton.Runtime.MessageExecutionScopeRunner>();
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scopes = new Schemata.Messaging.Skeleton.Runtime.MessageExecutionScopeFactory(host.GetRequiredService<IServiceScopeFactory>());
        await using var system = new InProcessActorSystem<SchemataActor>(host, new ActorRegistry(), scopes, Options.Create(new SchemataActorOptions()));
        await using var caller = host.CreateAsyncScope();
        var reference = await system.SpawnAsync(new("probe", "host"), new(typeof(PlainProbeActor)));
        await reference.AskAsync<Read, Guid?>(caller.ServiceProvider, new());
        Assert.NotEmpty(captureProviders);
        Assert.DoesNotContain(host, captureProviders);
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Tenant_Reference_Uses_The_Caller_Turn_Not_The_Restored_Send_Scope() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => ValueTask.FromResult(Scope(root, context)));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var probe = new TenantSelfAskProbe();
        var reference = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "self-ask"), new(typeof(TenantSelfAskActor), [probe]));
        probe.Self = reference;

        var error = await reference.AskAsync<TenantSelfAsk, string>(root, new(), Context(tenant), timeout: TimeSpan.FromSeconds(30));

        Assert.Contains("cannot await an Ask to itself", error, StringComparison.Ordinal);
        Assert.Equal(0, probe.EnqueueCount);
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Actor_Restore_Failure_Disposes_Created_Scope_And_Preserves_Cleanup_Failure() {
        var entry = new InvalidOperationException("restore");
        var cleanup = new InvalidOperationException("cleanup");
        var resource = new Mock<IAsyncDisposable>();
        resource.Setup(value => value.DisposeAsync()).Throws(cleanup);
        var injectTurnFailure = false;
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> _, IServiceProvider target, CancellationToken _) => {
                if (!injectTurnFailure) return ValueTask.CompletedTask;
                injectTurnFailure = false;
                target.GetRequiredService<IAsyncDisposable>();
                return ValueTask.FromException(entry);
            });
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddScoped(_ => resource.Object);
        services.AddScoped(_ => propagator.Object);
        services.AddSingleton(scopes.Object);
        services.AddSingleton<IMessageExecutionScopeRunner, Schemata.Messaging.Skeleton.Runtime.MessageExecutionScopeRunner>();
        await using var root = services.BuildServiceProvider();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((_, _) => ValueTask.FromResult(Scope(root, TenantIdentity.Host)));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actor = new Mock<IActor>();
        actor.Setup(value => value.OnStartedAsync(It.IsAny<IActorContext>())).Returns(async () => {
            started.TrySetResult();
            await release.Task;
        });
        var system = new InProcessActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var reference = await system.SpawnAsync(new("probe", "entry"), new(typeof(ActorAdapter), [actor.Object]));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        injectTurnFailure = true;
        var ask = reference.AskAsync<Read, Guid?>(root, new()).AsTask();
        release.SetResult();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => ask);
        Assert.Contains(entry, failure.Flatten().InnerExceptions);
        Assert.Contains(cleanup, failure.Flatten().InnerExceptions);
        await system.ShutdownAsync(default);
        actor.Verify(value => value.OnReceiveAsync(It.IsAny<IActorContext>(), It.IsAny<Envelope>()), Times.Never);
        resource.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Actor_Reference_Disposes_Scoped_Services_With_Their_Scoped_Identity() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var disposed = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        services.AddSingleton(disposed);
        services.AddScoped<DisposalProbe>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((_, _) => {
                var selected = Scope(root, tenant);
                var probe = selected.Services.GetRequiredService<DisposalProbe>();
                probe.Identity = tenant;
                return ValueTask.FromResult(selected);
            });
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var reference = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "dispose"), new(typeof(ProbeActor), [new Probe()]));
        Assert.Equal(tenant.Uid, await reference.AskAsync<Read, Guid?>(root, new(), Context(tenant)));
        Assert.Equal(tenant.Uid, await disposed.Task);
    }

    public sealed class ActorAdapter(IActor inner) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) => inner.OnStartedAsync(context);
        public ValueTask OnStoppedAsync(IActorContext context) => inner.OnStoppedAsync(context);
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => inner.OnFailedAsync(context, error);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => inner.OnReceiveAsync(context, envelope);
    }

    [Fact]
    public async Task Hosted_Idle_Collection_Retires_Both_Tenants_And_Retained_Refs_Reactivate() {
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => {
                return ValueTask.FromResult(Scope(root, context));
            });
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions { IdleTimeout = TimeSpan.FromSeconds(1), IdleScanInterval = TimeSpan.FromSeconds(1) }), clock);
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        var first = new Probe();
        var second = new Probe();
        var one = await system.SpawnAsync(a, Invocation(root, a), new("probe", "same"), new(typeof(ProbeActor), [first]));
        Assert.Equal(a.Uid, await one.AskAsync<Read, Guid?>(root, new(), Context(a)));
        var two = await system.SpawnAsync(b, Invocation(root, b), new("probe", "same"), new(typeof(ProbeActor), [second]));
        Assert.Equal(b.Uid, await two.AskAsync<Read, Guid?>(root, new(), Context(b)));
        using var cancellation = new CancellationTokenSource();
        var collection = system.CollectIdleAsync(cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!first.Stopped.Task.IsCompleted || !second.Stopped.Task.IsCompleted) {
            deadline.Token.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromSeconds(2));
            await Task.Yield();
        }
        Assert.Equal(a.Uid, await first.Stopped.Task);
        Assert.Equal(b.Uid, await second.Stopped.Task);
        Assert.Equal(a.Uid, await one.AskAsync<Read, Guid?>(root, new(), Context(a)));
        Assert.Equal(b.Uid, await two.AskAsync<Read, Guid?>(root, new(), Context(b)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection);
    }

    [Fact]
    public async Task Timer_And_Shutdown_Restore_Each_Activation_Context_And_Reject_Cross_Reference() {
        var callbacks = new List<(TimerCallback Callback, object? State)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(value => value.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, _, _) => {
                callbacks.Add((callback, state));
                return Mock.Of<ITimer>();
            });
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => {
                return ValueTask.FromResult(Scope(root, context));
            });
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions()), clock.Object);
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        var first = new Probe();
        var second = new Probe();
        var reference = await system.SpawnAsync(a, Invocation(root, a), new("probe", "same"), new(typeof(ProbeActor), [first]));
        Assert.Equal(a.Uid, await reference.AskAsync<Read, Guid?>(root, new(), Context(a)));
        var other = await system.SpawnAsync(b, Invocation(root, b), new("probe", "same"), new(typeof(ProbeActor), [second]));
        Assert.Equal(b.Uid, await other.AskAsync<Read, Guid?>(root, new(), Context(b)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reference.AskAsync<Read, Guid?>(root, new(), Context(b)).AsTask());
        var foreign = Context(b);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reference.AskAsync<Read, Guid?>(root, new(), foreign).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reference.TellAsync(root, new Read(), foreign).AsTask());
        Assert.Equal(a.Uid, await reference.AskAsync<Read, Guid?>(root, new(), Context(a)));
        foreach (var (callback, state) in callbacks) callback(state);
        Assert.Equal(a.Uid, await first.Tick.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(b.Uid, await second.Tick.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        await system.ShutdownAsync(default);
        Assert.Equal(a.Uid, await first.Stopped.Task);
        Assert.Equal(b.Uid, await second.Stopped.Task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(a, Invocation(root, a), reference.Id));
    }

    [Fact]
    public async Task Retained_Reference_Rebuilds_Across_Idle_With_Exactly_One_Owner() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => ValueTask.FromResult(Scope(root, context)));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions { IdleTimeout = TimeSpan.FromSeconds(1), IdleScanInterval = TimeSpan.FromSeconds(1) }), clock);
        var instance = new Instance();
        var retained = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "owner"), new(typeof(OwnerActor), [instance]));
        var first = await retained.AskAsync<Read, Guid?>(root, new(), Context(tenant));
        Assert.Equal(1, instance.Starts);

        using var cancellation = new CancellationTokenSource();
        var collection = system.CollectIdleAsync(cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!instance.Stopped.Task.IsCompleted) {
            deadline.Token.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
        Assert.Equal(tenant.Uid, await instance.Stopped.Task);
        for (var tick = 0; tick < 4; tick++) {
            deadline.Token.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection);

        var fresh = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "owner"), new(typeof(OwnerActor), [instance]));
        var rebuilt = await fresh.AskAsync<Read, Guid?>(root, new(), Context(tenant));
        Assert.NotEqual(first, rebuilt);
        Assert.Equal(2, instance.Starts);
        Assert.Equal(rebuilt, await retained.AskAsync<Read, Guid?>(root, new(), Context(tenant)));
        Assert.Equal(2, instance.Starts);
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Concurrent_Same_Tenant_Same_ActorId_References_Share_One_Activation() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => ValueTask.FromResult(Scope(root, context)));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var instance = new Instance();
        var one = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "twin"), new(typeof(OwnerActor), [instance]));
        var two = await system.SpawnAsync(tenant, Invocation(root, tenant), new("probe", "twin"), new(typeof(OwnerActor), [instance]));
        Assert.NotSame(one, two);

        Task<Guid?>[] replies = [one.AskAsync<Read, Guid?>(root, new(), Context(tenant)).AsTask(), two.AskAsync<Read, Guid?>(root, new(), Context(tenant)).AsTask()];

        var answered = await Task.WhenAll(replies);
        Assert.Equal(answered[0], answered[1]);
        Assert.Equal(1, instance.Starts);
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Parallel_Tenant_Operations_Preserve_Their_Own_Identity() {
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddSchemataActor();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => ValueTask.FromResult(Scope(root, context)));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var one = await system.SpawnAsync(a, Invocation(root, a), new("probe", "same"), new(typeof(ProbeActor), [new Probe()]));
        var two = await system.SpawnAsync(b, Invocation(root, b), new("probe", "same"), new(typeof(ProbeActor), [new Probe()]));

        var replies = await Task.WhenAll(one.AskAsync<Read, Guid?>(root, new(), Context(a)).AsTask(), two.AskAsync<Read, Guid?>(root, new(), Context(b)).AsTask());

        Assert.Equal(a.Uid, replies[0]);
        Assert.Equal(b.Uid, replies[1]);
        await system.ShutdownAsync(default);
    }

    private static MessageContext Context(TenantIdentity identity) => new(new Dictionary<string, string?> {
        [TenancyConstants.Messaging.TenantIdKey] = identity.Uid?.ToString("D") ?? "host",
    });

    private static IServiceProvider Invocation(IServiceProvider services, TenantIdentity identity) => new IdentityProvider(services, identity);

    private static MessageExecutionScope Scope(IServiceProvider services, MessageContext? context) {
        if (context?.Items.TryGetValue(TenancyConstants.Messaging.TenantIdKey, out var value) != true || value == "host") {
            return Scope(services, TenantIdentity.Host);
        }
        if (!Guid.TryParse(value, out var uid)) throw new TenantResolveException();
        return Scope(services, new TenantIdentity(uid));
    }

    private static MessageExecutionScope Scope(IServiceProvider services, TenantIdentity identity) =>
        new(new AsyncServiceScope(new IdentityScope(services.CreateAsyncScope(), identity)));

    private sealed class IdentityProvider(IServiceProvider services, TenantIdentity identity) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TenantIdentity) ? identity : services.GetService(serviceType);
    }

    private sealed class IdentityScope(AsyncServiceScope scope, TenantIdentity identity) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new IdentityProvider(scope.ServiceProvider, identity);
        public void Dispose() => scope.Dispose();
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private sealed class LifetimeProbe(List<Guid?> disposed) : IAsyncDisposable
    {
        public TenantIdentity Identity { get; set; }
        public async ValueTask DisposeAsync() {
            await Task.Yield();
            disposed.Add(Identity.Uid);
        }
    }

    private sealed class DisposalProbe(TaskCompletionSource<Guid?> disposed) : IAsyncDisposable
    {
        public TenantIdentity Identity { get; set; }
        public async ValueTask DisposeAsync() {
            await Task.Yield();
            disposed.TrySetResult(Identity.Uid);
        }
    }

    public sealed class Instance
    {
        private Guid _activation = Guid.NewGuid();

        /// <summary>Identity of the current activation; rotated at each start, so replies identify the activation that served them.</summary>
        public Guid Activation => _activation;

        public int Starts;
        public TaskCompletionSource<Guid?> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Rotate() {
            _activation = Guid.NewGuid();
            Interlocked.Increment(ref Starts);
        }
    }

    public sealed class OwnerActor(Instance instance) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) {
            instance.Rotate();
            return ValueTask.CompletedTask;
        }
        public ValueTask OnStoppedAsync(IActorContext context) { instance.Stopped.TrySetResult(context.Services.GetRequiredService<TenantIdentity>().Uid); return ValueTask.CompletedTask; }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(instance.Activation);
    }

    public sealed record Read : IRequest<Guid?>;
    public sealed class Probe
    {
        public TaskCompletionSource<Guid?> Tick { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Guid?> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed record TenantSelfAsk : IRequest<string>;

    public sealed class TenantSelfAskProbe
    {
        public IActorRef Self { get; set; } = null!;
        public int EnqueueCount { get; private set; }

        public void Enqueued() {
            EnqueueCount++;
        }
    }

    public sealed class TenantSelfAskActor(TenantSelfAskProbe probe) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);

        public async ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) {
            try {
                await probe.Self.AskAsync<TenantSelfAsk, string>(context.Services, new(), MessageContexts.Capture(context.Services), timeout: TimeSpan.FromSeconds(1));
                probe.Enqueued();
                await context.ReplyAsync("accepted");
            } catch (InvalidOperationException error) {
                await context.ReplyAsync(error.Message);
            }
        }
    }

    private sealed class PlainProbeActor : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(Guid.Empty);
    }

    public sealed class ProbeActor(Probe probe) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) {
            context.RegisterTimer("tick", turn => { probe.Tick.TrySetResult(turn.Services.GetRequiredService<TenantIdentity>().Uid); return ValueTask.CompletedTask; }, TimeSpan.Zero);
            return ValueTask.CompletedTask;
        }
        public ValueTask OnStoppedAsync(IActorContext context) { probe.Stopped.TrySetResult(context.Services.GetRequiredService<TenantIdentity>().Uid); return ValueTask.CompletedTask; }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(context.Services.GetRequiredService<TenantIdentity>().Uid);
    }
}

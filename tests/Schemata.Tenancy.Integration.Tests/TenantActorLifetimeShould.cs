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
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((_, _) => ValueTask.FromResult(
                new MessageExecutionScope(root.CreateAsyncScope(), () => TenantContext.Enter(tenant))));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions { IdleTimeout = TimeSpan.FromSeconds(1), IdleScanInterval = TimeSpan.FromSeconds(1) }), clock);
        using (TenantContext.Enter(tenant)) {
            var reference = await system.SpawnAsync(new("probe", "idle-failure"), new(typeof(IdleFailureActor), [failure, stopped]));
            Assert.Equal(tenant.Uid, await reference.AskAsync<Read, Guid?>(new()));
        }
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
        public ValueTask OnStartedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(TenantContext.Current.Uid);
        public async ValueTask DisposeAsync() {
            await Task.Yield();
            stopped.TrySetResult(TenantContext.Current.Uid);
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
            .Callback<IDictionary<string, string?>, IServiceProvider>((items, _) => {
                captured.Add(TenantContext.Current.Uid);
                items[TenancyConstants.Messaging.TenantIdKey] = TenantContext.Current.Uid!.Value.ToString("D");
            });
        var lifetime = new Mock<IAsyncDisposable>();
        lifetime.Setup(value => value.DisposeAsync()).Returns(() => {
            disposed.Add(TenantContext.Current.Uid);
            return ValueTask.CompletedTask;
        });
        await using var host = new ServiceCollection().AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var selectedServices = new ServiceCollection();
        selectedServices.AddScoped(_ => propagation.Object);
        selectedServices.AddScoped(_ => lifetime.Object);
        await using var selected = selectedServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => {
                var scope = selected.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IAsyncDisposable>();
                var identity = new TenantIdentity(Guid.Parse(context!.Items[TenancyConstants.Messaging.TenantIdKey]!));
                return ValueTask.FromResult(new MessageExecutionScope(scope, () => TenantContext.Enter(identity)));
            });
        await using var system = new TenantActorSystem<SchemataActor>(host, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        using (TenantContext.Enter(a)) {
            var actor = await system.SpawnAsync(new("probe", "same"), new(typeof(ProbeActor), [new Probe()]));
            Assert.Equal(a.Uid, await actor.AskAsync<Read, Guid?>(new()));
        }
        using (TenantContext.Enter(b)) {
            var actor = await system.SpawnAsync(new("probe", "same"), new(typeof(ProbeActor), [new Probe()]));
            Assert.Equal(b.Uid, await actor.AskAsync<Read, Guid?>(new()));
        }
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
        services.AddScoped(_ => propagation.Object);
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scopes = new Schemata.Messaging.Skeleton.Runtime.MessageExecutionScopeFactory(host.GetRequiredService<IServiceScopeFactory>());
        await using var system = new InProcessActorSystem<SchemataActor>(host, new ActorRegistry(), scopes, Options.Create(new SchemataActorOptions()));
        var reference = await system.SpawnAsync(new("probe", "host"), new(typeof(ProbeActor), [new Probe()]));
        await reference.AskAsync<Read, Guid?>(new());
        Assert.NotEmpty(captureProviders);
        Assert.DoesNotContain(host, captureProviders);
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Actor_Entry_Failure_Disposes_Created_Scope_And_Preserves_Cleanup_Failure() {
        var entry = new InvalidOperationException("entry");
        var cleanup = new InvalidOperationException("cleanup");
        var resource = new Mock<IAsyncDisposable>();
        resource.Setup(value => value.DisposeAsync()).Throws(cleanup);
        var services = new ServiceCollection();
        services.AddScoped(_ => resource.Object);
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        var injectTurnFailure = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((_, _) => {
                var scope = root.CreateAsyncScope();
                if (!injectTurnFailure) {
                    return ValueTask.FromResult(new MessageExecutionScope(scope, () => Mock.Of<IDisposable>()));
                }
                injectTurnFailure = false;
                scope.ServiceProvider.GetRequiredService<IAsyncDisposable>();
                return ValueTask.FromResult(new MessageExecutionScope(scope, () => throw entry));
            });
        var actor = new Mock<IActor>();
        actor.Setup(value => value.OnStartedAsync(It.IsAny<IActorContext>())).Returns(async () => {
            started.TrySetResult();
            await release.Task;
        });
        var system = new InProcessActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var reference = await system.SpawnAsync(new("probe", "entry"), new(typeof(ActorAdapter), [actor.Object]));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        injectTurnFailure = true;
        var ask = reference.AskAsync<Read, Guid?>(new()).AsTask();
        release.SetResult();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => ask);
        Assert.Contains(entry, failure.Flatten().InnerExceptions);
        Assert.Contains(cleanup, failure.Flatten().InnerExceptions);
        await system.ShutdownAsync(default);
        actor.Verify(value => value.OnReceiveAsync(It.IsAny<IActorContext>(), It.IsAny<Envelope>()), Times.Never);
        resource.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Actor_Reference_Disposes_Scoped_Services_While_Captured_Tenant_Is_Entered() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var disposed = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new Mock<IAsyncDisposable>();
        resource.Setup(value => value.DisposeAsync()).Returns(async () => {
            await Task.Yield();
            Assert.Equal(tenant.Uid, TenantContext.Current.Uid);
            disposed.TrySetResult(TenantContext.Current.Uid);
        });
        var services = new ServiceCollection();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        services.AddScoped(_ => resource.Object);
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((_, _) => {
                var scope = root.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IAsyncDisposable>();
                return ValueTask.FromResult(new MessageExecutionScope(scope, () => TenantContext.Enter(tenant)));
            });
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        using (TenantContext.Enter(tenant)) {
            var reference = await system.SpawnAsync(new("probe", "dispose"), new(typeof(ProbeActor), [new Probe()]));
            Assert.Equal(tenant.Uid, await reference.AskAsync<Read, Guid?>(new()));
        }
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
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => {
                var identity = new TenantIdentity(Guid.Parse(context!.Items[TenancyConstants.Messaging.TenantIdKey]!));
                return ValueTask.FromResult(new MessageExecutionScope(root.CreateAsyncScope(), () => TenantContext.Enter(identity)));
            });
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions { IdleTimeout = TimeSpan.FromSeconds(1), IdleScanInterval = TimeSpan.FromSeconds(1) }), clock);
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        var first = new Probe();
        var second = new Probe();
        IActorRef one;
        IActorRef two;
        using (TenantContext.Enter(a)) {
            one = await system.SpawnAsync(new("probe", "same"), new(typeof(ProbeActor), [first]));
            Assert.Equal(a.Uid, await one.AskAsync<Read, Guid?>(new()));
        }
        using (TenantContext.Enter(b)) {
            two = await system.SpawnAsync(new("probe", "same"), new(typeof(ProbeActor), [second]));
            Assert.Equal(b.Uid, await two.AskAsync<Read, Guid?>(new()));
        }
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
        using (TenantContext.Enter(a)) Assert.Equal(a.Uid, await one.AskAsync<Read, Guid?>(new()));
        using (TenantContext.Enter(b)) Assert.Equal(b.Uid, await two.AskAsync<Read, Guid?>(new()));
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
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => {
                var value = context!.Items[TenancyConstants.Messaging.TenantIdKey];
                var identity = value == "host" ? TenantIdentity.Host : new TenantIdentity(Guid.Parse(value!));
                return ValueTask.FromResult(new MessageExecutionScope(root.CreateAsyncScope(), () => TenantContext.Enter(identity)));
            });
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions()), clock.Object);
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        var first = new Probe();
        var second = new Probe();
        IActorRef reference;
        using (TenantContext.Enter(a)) {
            reference = await system.SpawnAsync(new("probe", "same"), new(typeof(ProbeActor), [first]));
            Assert.Equal(a.Uid, await reference.AskAsync<Read, Guid?>(new()));
        }
        using (TenantContext.Enter(b)) {
            var other = await system.SpawnAsync(new("probe", "same"), new(typeof(ProbeActor), [second]));
            Assert.Equal(b.Uid, await other.AskAsync<Read, Guid?>(new()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reference.AskAsync<Read, Guid?>(new()).AsTask());
        }
        using (TenantContext.Enter(a)) {
            var foreign = new MessageContext(new Dictionary<string, string?> {
                [TenancyConstants.Messaging.TenantIdKey] = b.Uid!.Value.ToString("D"),
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => reference.AskAsync<Read, Guid?>(new(), foreign).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => reference.TellAsync(new Read(), foreign).AsTask());
            Assert.Equal(a, TenantContext.Current);
            Assert.Equal(a.Uid, await reference.AskAsync<Read, Guid?>(new()));
        }
        foreach (var (callback, state) in callbacks) callback(state);
        Assert.Equal(a.Uid, await first.Tick.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(b.Uid, await second.Tick.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        await system.ShutdownAsync(default);
        Assert.Equal(a.Uid, await first.Stopped.Task);
        Assert.Equal(b.Uid, await second.Stopped.Task);
        using (TenantContext.Enter(a)) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(reference.Id));
        }
    }

    [Fact]
    public async Task Retained_Reference_Rebuilds_Across_Idle_With_Exactly_One_Owner() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => ValueTask.FromResult(
                new MessageExecutionScope(root.CreateAsyncScope(), () => TenantContext.Enter(new TenantIdentity(
                    Guid.Parse(context!.Items[TenancyConstants.Messaging.TenantIdKey]!))))));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object,
            Options.Create(new SchemataActorOptions { IdleTimeout = TimeSpan.FromSeconds(1), IdleScanInterval = TimeSpan.FromSeconds(1) }), clock);
        var instance = new Instance();
        IActorRef retained;
        Guid? first;
        using (TenantContext.Enter(tenant)) {
            retained = await system.SpawnAsync(new("probe", "owner"), new(typeof(OwnerActor), [instance]));
            first = await retained.AskAsync<Read, Guid?>(new());
        }
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

        using (TenantContext.Enter(tenant)) {
            var fresh = await system.SpawnAsync(new("probe", "owner"), new(typeof(OwnerActor), [instance]));
            var rebuilt = await fresh.AskAsync<Read, Guid?>(new());
            Assert.NotEqual(first, rebuilt);
            Assert.Equal(2, instance.Starts);
            Assert.Equal(rebuilt, await retained.AskAsync<Read, Guid?>(new()));
            Assert.Equal(2, instance.Starts);
        }
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Concurrent_Same_Tenant_Same_ActorId_References_Share_One_Activation() {
        var tenant = new TenantIdentity(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>();
        await using var root = services.BuildServiceProvider();
        var scopes = new Mock<IMessageExecutionScopeFactory>();
        scopes.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext?, CancellationToken>((context, _) => ValueTask.FromResult(
                new MessageExecutionScope(root.CreateAsyncScope(), () => TenantContext.Enter(new TenantIdentity(
                    Guid.Parse(context!.Items[TenancyConstants.Messaging.TenantIdKey]!))))));
        await using var system = new TenantActorSystem<SchemataActor>(root, new ActorRegistry(), scopes.Object, Options.Create(new SchemataActorOptions()));
        var instance = new Instance();
        IActorRef one;
        IActorRef two;
        using (TenantContext.Enter(tenant)) {
            one = await system.SpawnAsync(new("probe", "twin"), new(typeof(OwnerActor), [instance]));
            two = await system.SpawnAsync(new("probe", "twin"), new(typeof(OwnerActor), [instance]));
        }
        Assert.NotSame(one, two);

        Task<Guid?>[] replies;
        using (TenantContext.Enter(tenant)) {
            replies = [one.AskAsync<Read, Guid?>(new()).AsTask(), two.AskAsync<Read, Guid?>(new()).AsTask()];
        }

        var answered = await Task.WhenAll(replies);
        Assert.Equal(answered[0], answered[1]);
        Assert.Equal(1, instance.Starts);
        await system.ShutdownAsync(default);
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
        public ValueTask OnStoppedAsync(IActorContext context) { instance.Stopped.TrySetResult(TenantContext.Current.Uid); return ValueTask.CompletedTask; }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(instance.Activation);
    }

    public sealed record Read : IRequest<Guid?>;
    public sealed class Probe
    {
        public TaskCompletionSource<Guid?> Tick { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Guid?> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class ProbeActor(Probe probe) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) {
            context.RegisterTimer("tick", _ => { probe.Tick.TrySetResult(TenantContext.Current.Uid); return ValueTask.CompletedTask; }, TimeSpan.Zero);
            return ValueTask.CompletedTask;
        }
        public ValueTask OnStoppedAsync(IActorContext context) { probe.Stopped.TrySetResult(TenantContext.Current.Uid); return ValueTask.CompletedTask; }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => context.ReplyAsync(TenantContext.Current.Uid);
    }
}

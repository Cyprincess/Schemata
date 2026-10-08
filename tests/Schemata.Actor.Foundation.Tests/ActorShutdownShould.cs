using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

[Trait("Layer", "Integration")]
public class ActorShutdownShould
{
    [Fact]
    public async Task Cancellation_And_Stopped_Failures_Preserve_Cleanup_And_Ownership() {
        var (system, _, root) = ActorSystemFactory.Create();
        using var services = (ServiceProvider)root;
        var callback = new InvalidOperationException("cancel");
        var stopped = new InvalidOperationException("stopped");
        var entered = Signal();
        var release = Signal();
        var actor = new Mock<IActor>();
        var disposed = actor.As<IDisposable>();
        actor.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Callback<IActorContext>(ctx => {
            ctx.Stopping.Register(() => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); throw callback; });
        }).Returns(ValueTask.CompletedTask);
        actor.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Throws(stopped);
        var reference = await SpawnActivation(system, new("cleanup", "one"), new(typeof(ForwardActor), [actor.Object]));
        var first = reference.StopAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = reference.StopAsync();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        release.SetResult();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains(callback, failure.Flatten().InnerExceptions);
        Assert.Contains(stopped, failure.Flatten().InnerExceptions);
        await Assert.ThrowsAsync<AggregateException>(() => second);
        actor.Verify(a => a.OnStoppedAsync(It.IsAny<IActorContext>()), Times.Once);
        disposed.Verify(a => a.Dispose(), Times.Once);
        var replacement = await system.SpawnAsync(reference.Id, new(typeof(IdentityActor)));
        Assert.NotSame(reference, replacement);
        await system.StopAsync(reference.Id);
    }

    [Fact]
    public async Task Host_Deadline_Faults_Asks_But_Retains_Running_Turn_Until_Released() {
        using var host = new HostBuilder().ConfigureServices(services => services.AddSchemataActor()).Build();
        await host.StartAsync();
        var system = (InProcessActorSystem)host.Services.GetRequiredService<IActorSystem>();
        var gate = new ManualGate();
        var actor = await SpawnActivation(system, new("deadline", "one"), new(typeof(GatedActor), [gate]));
        var executing = actor.AskAsync<GateAndWait, string>(new()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = actor.AskAsync<Increment, int>(new()).AsTask();
        using var budget = new CancellationTokenSource();
        var stopping = host.StopAsync(budget.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(actor.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.TellAsync(new Increment()).AsTask());
        budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => executing.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(actor.Completion.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SpawnAsync(actor.Id, new(typeof(IdentityActor))));
        gate.Release();
        await actor.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StopApplication_Closes_Admission_And_Graceful_Stop_Drains_Accepted_Work() {
        using var host = new HostBuilder().ConfigureServices(services => services.AddSchemataActor()).Build();
        await host.StartAsync();
        var system = host.Services.GetRequiredService<IActorSystem>();
        var gate = new ManualGate();
        var actor = await system.SpawnAsync(new("drain", "one"), new(typeof(GatedActor), [gate]));
        var executing = actor.AskAsync<GateAndWait, string>(new()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = actor.AskAsync<Increment, int>(new()).AsTask();
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(actor.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SpawnAsync(new("new", "one"), new(typeof(IdentityActor))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Increment, int>(new()).AsTask());
        gate.Release();
        Assert.Equal("released", await executing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, await queued.WaitAsync(TimeSpan.FromSeconds(5)));
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Host_Shutdown_Retains_Failures_From_All_Actors() {
        using var host = new HostBuilder().ConfigureServices(services => services.AddSchemataActor()).Build();
        await host.StartAsync();
        var system = host.Services.GetRequiredService<IActorSystem>();
        var failures = new[] { new InvalidOperationException("first"), new InvalidOperationException("second") };
        for (var i = 0; i < failures.Length; i++) {
            var actor = new Mock<IActor>();
            actor.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
            actor.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Throws(failures[i]);
            await system.SpawnAsync(new("fail", i.ToString()), new(typeof(ForwardActor), [actor.Object]));
        }
        var observed = await Assert.ThrowsAsync<AggregateException>(() => host.StopAsync());
        Assert.All(failures, failure => Assert.Contains(failure, observed.Flatten().InnerExceptions));
    }

    [Fact]
    public async Task Startup_Cancellation_And_Stop_Failures_All_Remain_Observable() {
        var (system, _, root) = ActorSystemFactory.Create();
        using var services = (ServiceProvider)root;
        var start = new InvalidOperationException("start");
        var cancel = new InvalidOperationException("cancel");
        var stop = new InvalidOperationException("stop");
        var started = Signal();
        var release = Signal();
        var mock = new Mock<IActor>();
        mock.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Returns<IActorContext>(async ctx => {
            ctx.Stopping.Register(() => throw cancel);
            started.SetResult();
            await release.Task;
            throw start;
        });
        mock.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Throws(stop);
        var actor = await SpawnActivation(system, new("startfail", "one"), new(typeof(ForwardActor), [mock.Object]));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = actor.AskAsync<Increment, int>(new()).AsTask();
        var stopping = actor.StopAsync();
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => queued);
        var errors = await Assert.ThrowsAsync<AggregateException>(() => stopping);
        Assert.All(new[] { start, cancel, stop }, error => Assert.Contains(error, errors.Flatten().InnerExceptions));
        mock.Verify(a => a.OnReceiveAsync(It.IsAny<IActorContext>(), It.IsAny<Envelope>()), Times.Never);
        mock.Verify(a => a.OnStoppedAsync(It.IsAny<IActorContext>()), Times.Once);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Deadline_While_Constructing_Still_Retires_The_Late_Activation() {
        var (system, _, root) = ActorSystemFactory.Create();
        using var services = (ServiceProvider)root;
        var gate = new ConstructionGate();
        var spawning = Task.Run(() => system.SpawnAsync(new("constructing", "one"),
            new(typeof(GatedConstructionActor), [gate, new SharedCounter()])));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        using var budget = new CancellationTokenSource();
        var stopping = system.ShutdownAsync(budget.Token);
        await Task.Run(() => budget.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(5)));
        gate.Release();
        var actor = await spawning.WaitAsync(TimeSpan.FromSeconds(5));
        await system.ShutdownAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<WhoAmI, Guid>(new()).AsTask());
    }

    [Fact]
    public async Task One_Blocked_Cancellation_Callback_Does_Not_Block_Other_Actor_Notification() {
        var (system, _, root) = ActorSystemFactory.Create();
        using var services = (ServiceProvider)root;
        var entered = Signal();
        var release = Signal();
        var ready = Signal();
        var notified = Signal();
        var blocked = new Mock<IActor>();
        blocked.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Callback<IActorContext>(ctx => {
            ctx.Stopping.Register(() => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); });
            ready.SetResult();
        }).Returns(ValueTask.CompletedTask);
        blocked.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
        var first = await SpawnActivation(system, new("blocked", "one"), new(typeof(ForwardActor), [blocked.Object]));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = new Mock<IActor>();
        other.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
        other.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Callback(() => notified.SetResult()).Returns(ValueTask.CompletedTask);
        await system.SpawnAsync(new("other", "one"), new(typeof(ForwardActor), [other.Object]));
        var shutdown = system.ShutdownAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.Completion.IsCompleted);
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        blocked.Verify(a => a.OnStoppedAsync(It.IsAny<IActorContext>()), Times.Once);
    }

    [Fact]
    public async Task Supervision_And_Turn_Disposal_Failures_Are_Both_Retained() {
        var receive = new InvalidOperationException("receive");
        var supervision = new InvalidOperationException("supervision");
        var cleanup = new InvalidOperationException("cleanup");
        var resource = new Mock<IAsyncDisposable>();
        resource.Setup(r => r.DisposeAsync()).Throws(cleanup);
        var (system, _, root) = ActorSystemFactory.Create(services => services.AddScoped(_ => resource.Object));
        await using var services = (ServiceProvider)root;
        var actor = new Mock<IActor>();
        actor.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
        actor.Setup(a => a.OnReceiveAsync(It.IsAny<IActorContext>(), It.IsAny<Envelope>()))
            .Callback<IActorContext, Envelope>((ctx, _) => ctx.Services.GetRequiredService<IAsyncDisposable>()).Throws(receive);
        actor.Setup(a => a.OnFailedAsync(It.IsAny<IActorContext>(), receive)).Throws(supervision);
        actor.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
        var reference = await SpawnActivation(system, new("cleanup", "turn"), new(typeof(ForwardActor), [actor.Object]));
        Assert.Same(receive, await Assert.ThrowsAsync<InvalidOperationException>(() => reference.AskAsync<Increment, int>(new()).AsTask()));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => reference.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.All(new[] { receive, supervision, cleanup }, error => Assert.Contains(error, failure.Flatten().InnerExceptions));
        actor.Verify(a => a.OnStoppedAsync(It.IsAny<IActorContext>()), Times.Once);
        resource.Verify(r => r.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Ask_Cancellation_Returns_While_Item_Callback_Blocks_And_Retirement_Waits() {
        var (system, _, root) = ActorSystemFactory.Create();
        using var services = (ServiceProvider)root;
        var entered = Signal();
        var callback = Signal();
        var release = Signal();
        var finish = Signal();
        var failure = new InvalidOperationException("item cancellation");
        var actor = new Mock<IActor>();
        actor.Setup(a => a.OnStartedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
        actor.Setup(a => a.OnStoppedAsync(It.IsAny<IActorContext>())).Returns(ValueTask.CompletedTask);
        actor.Setup(a => a.OnReceiveAsync(It.IsAny<IActorContext>(), It.IsAny<Envelope>())).Returns<IActorContext, Envelope>(async (ctx, _) => {
            ctx.Stopping.Register(() => { callback.SetResult(); release.Task.GetAwaiter().GetResult(); throw failure; });
            entered.SetResult();
            await finish.Task;
            await ctx.ReplyAsync(1);
        });
        var reference = await SpawnActivation(system, new("cancel", "item"), new(typeof(ForwardActor), [actor.Object]));
        using var cancellation = new CancellationTokenSource();
        var asking = reference.AskAsync<Increment, int>(new(), ct: cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking.WaitAsync(TimeSpan.FromSeconds(5)));
        await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = reference.StopAsync();
        finish.SetResult();
        Assert.False(stopping.IsCompleted);
        release.SetResult();
        var errors = await Assert.ThrowsAsync<AggregateException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains(failure, errors.Flatten().InnerExceptions);
        actor.Verify(a => a.OnStoppedAsync(It.IsAny<IActorContext>()), Times.Once);
    }

    private static async Task<ActorInstance> SpawnActivation(InProcessActorSystem system, ActorId id, Props props) {
        await system.SpawnAsync(id, props);
        return await system.ResolveForSendAsync(id, props, default);
    }

    public sealed class ForwardActor(IActor inner) : IActor, IDisposable
    {
        public ValueTask OnStartedAsync(IActorContext ctx) => inner.OnStartedAsync(ctx);
        public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) => inner.OnReceiveAsync(ctx, envelope);
        public ValueTask OnStoppedAsync(IActorContext ctx) => inner.OnStoppedAsync(ctx);
        public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception error) => inner.OnFailedAsync(ctx, error);
        public void Dispose() { if (inner is IDisposable disposable) disposable.Dispose(); }
    }
}

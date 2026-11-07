using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionLeaseShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scope_Disposal_Uses_Restored_Scope_State_And_Preserves_Operation_Failure(bool fail) {
        var disposed = new List<string?>();
        var operation = new InvalidOperationException("renewal");
        var cleanup = new InvalidOperationException("disposal");
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TryRenewLeaseAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(() => fail ? Task.FromException<bool>(operation) : Task.FromResult(true));
        await using var root = new ServiceCollection().AddSingleton(store.Object).AddSingleton(disposed)
            .AddSingleton(new CleanupOptions(fail ? cleanup : null))
            .AddScoped<ExecutionMarker>().AddScoped<DisposalProbe>().AddSingleton<IMessageContextPropagator, MarkerPropagator>()
            .BuildServiceProvider();
        var factory = new Mock<IMessageExecutionScopeFactory>();
        factory.Setup(f => f.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns((MessageContext? _, CancellationToken _) => {
                var scope = root.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<DisposalProbe>();
                return ValueTask.FromResult(new MessageExecutionScope(scope));
            });
        await using var lease = new JobExecutionLease<SchemataJob, SchemataJobExecution>(
            JobExecutionScopes.Messaging(factory.Object, new(new Dictionary<string, string?>())),
            new() { Uid = Guid.NewGuid(), LeaseExpireTime = DateTime.UtcNow.AddHours(1) },
            TimeSpan.FromHours(1), TimeProvider.System, new ActiveExecutionRegistry(), default);
        if (fail) {
            var error = await Assert.ThrowsAsync<AggregateException>(() => lease.ConfirmAsync(default));
            Assert.Same(operation, error.InnerExceptions[0]);
            Assert.Same(cleanup, error.InnerExceptions[1]);
        } else {
            await lease.ConfirmAsync(default);
        }
        Assert.Equal(["captured"], disposed);
    }

    private sealed class ExecutionMarker
    {
        public string? Value { get; set; }
    }

    private sealed class MarkerPropagator : IMessageContextPropagator
    {
        public void Capture(IDictionary<string, string?> items, IServiceProvider source) { }
        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
            target.GetRequiredService<ExecutionMarker>().Value = "captured";
            return ValueTask.CompletedTask;
        }
    }

    private sealed record CleanupOptions(Exception? Error);

    private sealed class DisposalProbe(ExecutionMarker marker, List<string?> disposed, CleanupOptions options) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() {
            disposed.Add(marker.Value);
            if (marker.Value != "captured") return ValueTask.FromException(new InvalidOperationException("Scope state was not restored."));
            return options.Error is { } error ? ValueTask.FromException(error) : ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Confirmation_And_Terminal_Write_Restore_The_Persisted_Generic_Context_In_Fresh_Scopes() {
        var restored = new HashSet<IServiceProvider>();
        IServiceProvider? current = null;
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(p => p.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> items, IServiceProvider services, CancellationToken _) => {
                Assert.Equal("persisted", items["custom.execution"]);
                Assert.True(restored.Add(services));
                current = services;
                return ValueTask.CompletedTask;
            });
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TryRenewLeaseAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Assert.NotNull(current); return Task.FromResult(true); });
        store.Setup(s => s.TrySettleAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Assert.Equal(2, restored.Count); return Task.FromResult(true); });
        await using var services = new ServiceCollection().AddSingleton(store.Object).AddSingleton(propagator.Object).BuildServiceProvider();
        var execution = new SchemataJobExecution { Uid = Guid.NewGuid(), LeaseExpireTime = DateTime.UtcNow.AddHours(1),
            MessageContext = new() { ["custom.execution"] = "persisted" } };
        await using var lease = new JobExecutionLease<SchemataJob, SchemataJobExecution>(
            JobExecutionScopes.Messaging(new MessageExecutionScopeFactory(services.GetRequiredService<IServiceScopeFactory>()),
                new(execution.MessageContext ?? new())),
            execution, TimeSpan.FromHours(1), TimeProvider.System, new ActiveExecutionRegistry(), default);
        await lease.ConfirmAsync(default);
        Assert.True(await lease.TransitionAsync(selected => selected.TrySettleAsync(execution, default)));
        Assert.Equal(2, restored.Count);
    }


    [Fact]
    public async Task Confirmed_Renewal_Extends_Ownership_Beyond_Initial_Claim() {
        var now = DateTimeOffset.UnixEpoch;
        var callbacks = new List<(TimerCallback Callback, object? State)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(() => now);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, _, _) => {
                callbacks.Add((callback, state)); return Mock.Of<ITimer>();
            });
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expiryTimer = new Mock<ITimer>();
        expiryTimer.Setup(t => t.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>())).Callback(() => renewed.TrySetResult()).Returns(true);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), TimeSpan.FromSeconds(10), It.IsAny<TimeSpan>()))
            .Returns(expiryTimer.Object);
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TryRenewLeaseAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        await using var lease = new JobExecutionLease<SchemataJob, SchemataJobExecution>(JobExecutionScopes.Plain(services.GetRequiredService<IServiceScopeFactory>()),
            new() { Uid = Guid.NewGuid(), LeaseExpireTime = DateTime.UnixEpoch.AddSeconds(10) }, TimeSpan.FromSeconds(10), clock.Object, new ActiveExecutionRegistry(), CancellationToken.None);
        now = now.AddSeconds(6);
        var timer = Assert.Single(callbacks);
        timer.Callback(timer.State);
        await renewed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        now = now.AddSeconds(5);
        lease.ThrowIfOwnershipLost();
        Assert.False(lease.Token.IsCancellationRequested);
        Assert.True(await lease.TransitionAsync(_ => Task.FromResult(true)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_Or_Faulting_Renewal_Stops_Owner_And_Prevents_Settlement(bool fault) {
        var now = DateTimeOffset.UnixEpoch;
        var callbacks = new List<(TimerCallback Callback, object? State, TimeSpan Due)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(() => now);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, due, _) => {
                callbacks.Add((callback, state, due));
                return Mock.Of<ITimer>();
            });
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TryRenewLeaseAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(() => fault ? Task.FromException<bool>(new InvalidOperationException("renewal failed")) : Task.FromResult(false));
        await using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        var row = new SchemataJobExecution { Uid = Guid.NewGuid(), State = ExecutionState.Running,
            LeaseExpireTime = now.UtcDateTime.AddSeconds(10) };
        var running = new ActiveExecutionRegistry();
        var lease = new JobExecutionLease<SchemataJob, SchemataJobExecution>(JobExecutionScopes.Plain(services.GetRequiredService<IServiceScopeFactory>()), row, TimeSpan.FromSeconds(10), clock.Object, running, CancellationToken.None);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lease.Token.Register(() => cancelled.TrySetResult());
        var renewalTimer = Assert.Single(callbacks, value => value.Due == TimeSpan.FromSeconds(5));
        renewalTimer.Callback(renewalTimer.State);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var applied = false;
        Assert.False(await lease.TransitionAsync(_ => { applied = true; return Task.FromResult(true); }));
        Assert.False(applied);
        if (fault) await Assert.ThrowsAsync<InvalidOperationException>(() => lease.DisposeAsync().AsTask());
        else await lease.DisposeAsync();
        Assert.False(running.TryRemove(row.Uid.ToString("n"), out _));
    }

    [Fact]
    public async Task Expiry_Cancels_Work_Even_While_Renewal_Is_Unconfirmed() {
        var callbacks = new List<(TimerCallback Callback, object? State, TimeSpan Due)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UnixEpoch);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, due, _) => {
                callbacks.Add((callback, state, due)); return Mock.Of<ITimer>();
            });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TryRenewLeaseAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(async (SchemataJobExecution _, DateTime _, CancellationToken token) => {
                entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return true;
            });
        await using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        await using var lease = new JobExecutionLease<SchemataJob, SchemataJobExecution>(JobExecutionScopes.Plain(services.GetRequiredService<IServiceScopeFactory>()),
            new() { Uid = Guid.NewGuid(), LeaseExpireTime = DateTime.UnixEpoch.AddSeconds(10) }, TimeSpan.FromSeconds(10),
            clock.Object, new ActiveExecutionRegistry(), CancellationToken.None);
        var renewal = Assert.Single(callbacks, value => value.Due == TimeSpan.FromSeconds(5));
        renewal.Callback(renewal.State);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var expiry = Assert.Single(callbacks, value => value.Due == TimeSpan.FromSeconds(10));
        expiry.Callback(expiry.State);
        Assert.True(lease.Token.IsCancellationRequested);
        Assert.False(await lease.TransitionAsync(_ => Task.FromResult(true)));
    }
}

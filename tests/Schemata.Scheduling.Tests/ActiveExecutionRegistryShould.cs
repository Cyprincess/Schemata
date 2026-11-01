using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton.Services;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class ActiveExecutionRegistryShould
{
    [Fact]
    public void Second_Registration_For_One_Execution_Is_Rejected_Without_Taking_The_Source() {
        var registry = new ActiveExecutionRegistry();
        using var first = new TrackedSource();
        using var second = new TrackedSource();
        using var ownership = registry.Register("execution", first);

        var error = Assert.Throws<InvalidOperationException>(() => registry.Register("execution", second));

        Assert.Contains("local owner", error.Message);
        registry.Cancel("execution");
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Handle_Disposal_Removes_The_Entry_And_Disposes_The_Source_Once() {
        var registry = new ActiveExecutionRegistry();
        var source = new TrackedSource();
        var ownership = registry.Register("execution", source);

        ownership.Dispose();
        ownership.Dispose();

        Assert.Equal(1, source.Disposed);
        Assert.Throws<ObjectDisposedException>(() => source.Cancel());
        Assert.False(registry.Cancel("execution"));
    }

    [Fact]
    public void Direct_Removal_Wins_The_Source_And_Leaves_The_Handle_Without_Disposal() {
        var registry = new ActiveExecutionRegistry();
        var source = new TrackedSource();
        using var ownership = registry.Register("execution", source);

        Assert.True(registry.TryRemove("execution", out var removed));
        Assert.Same(source, removed);
        Assert.False(registry.TryRemove("execution", out var missing));
        Assert.Null(missing);

        ownership.Dispose();

        Assert.Equal(0, source.Disposed);
        source.Cancel();
        Assert.True(source.IsCancellationRequested);
        source.Dispose();
    }

    [Fact]
    public void Stale_Handle_Does_Not_Remove_A_Later_Registration_Of_The_Same_Name() {
        var registry = new ActiveExecutionRegistry();
        var first = new TrackedSource();
        var stale = registry.Register("execution", first);
        Assert.True(registry.TryRemove("execution", out var removed));

        var second = new TrackedSource();
        using var current = registry.Register("execution", second);
        stale.Dispose();

        Assert.False(second.IsCancellationRequested);
        Assert.True(registry.TryRemove("execution", out var released));
        Assert.Same(second, released);

        current.Dispose();
        released!.Dispose();
        removed!.Dispose();
        Assert.Equal(1, first.Disposed);
        Assert.Equal(1, second.Disposed);
    }

    [Fact]
    public void Cancellation_Reaches_The_Source_Before_The_Owner_Removes_It() {
        var registry = new ActiveExecutionRegistry();
        var source = new TrackedSource();
        using var ownership = registry.Register("execution", source);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callback = source.Token.Register(() => observed.TrySetResult());

        Assert.True(registry.Cancel("execution"));
        Assert.True(observed.Task.IsCompletedSuccessfully);
        Assert.True(registry.TryRemove("execution", out var removed));
        Assert.Same(source, removed);
        Assert.False(registry.Cancel("execution"));

        removed!.Dispose();
        Assert.Equal(1, source.Disposed);
    }

    [Fact]
    public async Task Concurrent_Registration_Removal_And_Cancellation_Dispose_Each_Source_Once() {
        var registry = new ActiveExecutionRegistry();

        for (var index = 0; index < 200; index++) {
            var name = $"execution-{index}";
            var source = new TrackedSource();
            var ownership = registry.Register(name, source);
            using var barrier = new Barrier(3);

            var releasing = Task.Run(() => {
                barrier.SignalAndWait();
                ownership.Dispose();
            });
            var removing = Task.Run(() => {
                barrier.SignalAndWait();
                if (registry.TryRemove(name, out var owned)) {
                    owned!.Dispose();
                }
            });
            var cancelling = Task.Run(() => {
                barrier.SignalAndWait();
                registry.Cancel(name);
            });

            await Task.WhenAll(releasing, removing, cancelling);

            Assert.Equal(1, source.Disposed);
            Assert.False(registry.TryRemove(name, out _));
        }
    }

    [Fact]
    public async Task Contended_Registration_Admits_One_Owner_And_The_Rejected_Caller_Disposes_Its_Source() {
        var registry = new ActiveExecutionRegistry();

        for (var index = 0; index < 200; index++) {
            var name = $"contended-{index}";
            var first = new TrackedSource();
            var second = new TrackedSource();
            TrackedSource? admitted = null;
            using var barrier = new Barrier(2);

            Task<IDisposable?> RegisterAsync(TrackedSource source) => Task.Run(() => {
                barrier.SignalAndWait();
                try {
                    var ownership = registry.Register(name, source);
                    Volatile.Write(ref admitted, source);
                    return (IDisposable?)ownership;
                } catch (InvalidOperationException) {
                    source.Dispose();
                    return null;
                }
            });

            var ownerships = await Task.WhenAll(RegisterAsync(first), RegisterAsync(second));

            var owner = ownerships[0] ?? ownerships[1];
            var rejectedHandle = ownerships[0] is null ? ownerships[0] : ownerships[1];
            Assert.NotNull(owner);
            Assert.Null(rejectedHandle);
            Assert.NotNull(admitted);
            var rejected = ReferenceEquals(admitted, first) ? second : first;

            Assert.Equal(1, rejected.Disposed);
            Assert.Throws<ObjectDisposedException>(() => rejected.Cancel());
            Assert.False(rejected.IsCancellationRequested);
            Assert.True(registry.Cancel(name));
            Assert.True(admitted!.IsCancellationRequested);

            owner.Dispose();
            Assert.Equal(1, admitted.Disposed);
            Assert.False(registry.TryRemove(name, out _));
        }
    }

    [Fact]
    public void Scheduling_Registers_The_Typed_Registry_As_A_Singleton() {
        using var provider = new ServiceCollection().AddSchemataScheduling().BuildServiceProvider();

        var registry = provider.GetRequiredService<IActiveExecutionRegistry>();

        Assert.IsType<ActiveExecutionRegistry>(registry);
        using var scope = provider.CreateScope();
        Assert.Same(registry, scope.ServiceProvider.GetRequiredService<IActiveExecutionRegistry>());
        Assert.Null(provider.GetService<ConcurrentDictionary<string, CancellationTokenSource>>());
    }

    private sealed class TrackedSource : CancellationTokenSource
    {
        private int _disposed;

        internal int Disposed => Volatile.Read(ref _disposed);

        protected override void Dispose(bool disposing) {
            Interlocked.Increment(ref _disposed);
            base.Dispose(disposing);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

[Trait("Layer", "Integration")]
public class MessageExecutionScopeShould
{
    [Fact]
    public async Task Failed_Bootstrap_Cleanup_Disposes_Selected_Services_Under_Their_Entered_Frame() {
        var ambient = new AsyncLocal<string?>();
        ambient.Value = "caller";
        var bootstrapError = new InvalidOperationException("bootstrap");
        var cleanupError = new ApplicationException("selected cleanup");
        var bootstrap = new Mock<IServiceScope>();
        bootstrap.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.FromException(bootstrapError));
        var bootstrapServices = new ServiceCollection().BuildServiceProvider();
        bootstrap.SetupGet(value => value.ServiceProvider).Returns(bootstrapServices);
        var bootstrapFactory = new Mock<IServiceScopeFactory>();
        bootstrapFactory.Setup(value => value.CreateScope()).Returns(bootstrap.Object);
        var source = new Mock<IServiceProvider>();
        source.Setup(value => value.GetService(typeof(IServiceScopeFactory))).Returns(bootstrapFactory.Object);
        var selected = new Mock<IServiceScope>();
        selected.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(() => {
            Assert.Equal("selected", ambient.Value);
            return ValueTask.FromException(cleanupError);
        });
        var lease = new Mock<IDisposable>();
        lease.Setup(value => value.Dispose()).Callback(() => ambient.Value = "caller");
        var factory = new Mock<IMessageExecutionScopeFactory>();
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(new MessageExecutionScope(new AsyncServiceScope(selected.Object), () => {
                ambient.Value = "selected";
                return lease.Object;
            })));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => MessageContexts.CaptureAsync(source.Object, scopes: factory.Object).AsTask());
        Assert.Contains(bootstrapError, failure.Flatten().InnerExceptions);
        Assert.Contains(cleanupError, failure.Flatten().InnerExceptions);
        Assert.Equal("caller", ambient.Value);
        selected.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        lease.Verify(value => value.Dispose(), Times.Once);
        bootstrapServices.Dispose();
    }

    [Fact]
    public async Task No_Feature_Captures_Nothing_And_Accepts_Arbitrary_Context() {
        using var root = new ServiceCollection().BuildServiceProvider();
        Assert.Empty(MessageContexts.Capture(root).Items);
        var context = new MessageContext(new Dictionary<string, string?> { ["custom.identity"] = "external" });
        var factory = new MessageExecutionScopeFactory(root.GetRequiredService<IServiceScopeFactory>());
        await using var scope = await factory.CreateAsync(context);
        using (scope.Enter()) await scope.RestoreAsync(context);
    }

    [Fact]
    public async Task Async_Capture_Resolves_Scoped_Propagators_In_Owned_Scopes_With_Root_Validation() {
        var captures = 0;
        var restores = 0;
        var disposals = 0;
        using var root = new ServiceCollection()
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
            .AddScoped<IMessageContextPropagator>(_ => {
                var propagator = new Mock<IMessageContextPropagator>();
                propagator.As<IDisposable>().Setup(value => value.Dispose()).Callback(() => disposals++);
                propagator.Setup(value => value.Capture(It.IsAny<IDictionary<string, string?>>(), It.IsAny<IServiceProvider>()))
                    .Callback((IDictionary<string, string?> items, IServiceProvider _) => items["test.scoped"] = (++captures).ToString());
                propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                    .Returns((IReadOnlyDictionary<string, string?> items, IServiceProvider _, CancellationToken _) => {
                        Assert.Equal("1", items["test.scoped"]);
                        restores++;
                        return ValueTask.CompletedTask;
                    });
                return propagator.Object;
            }).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var context = await MessageContexts.CaptureAsync(root);
        Assert.Equal("2", context.Items["test.scoped"]);
        Assert.Equal(1, restores);
        Assert.Equal(2, disposals);
    }

    [Fact]
    public async Task Each_Enter_Owns_A_Lease_And_Restore_Runs_Under_The_Caller_Frame() {
        var ambient = new AsyncLocal<string?>();
        var leases = new List<Mock<IDisposable>>();
        var restored = new List<string>();
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> _, IServiceProvider _, CancellationToken _) => {
                restored.Add(ambient.Value!);
                return ValueTask.CompletedTask;
            });
        using var root = new ServiceCollection().AddSingleton(propagator.Object).BuildServiceProvider();
        var scope = new MessageExecutionScope(root.CreateAsyncScope(), () => {
            var previous = ambient.Value;
            ambient.Value = "captured";
            var lease = new Mock<IDisposable>();
            lease.Setup(value => value.Dispose()).Callback(() => ambient.Value = previous);
            leases.Add(lease);
            return lease.Object;
        });
        ambient.Value = "parent";
        var message = new MessageContext(new Dictionary<string, string?>());
        using (scope.Enter()) {
            await scope.RestoreAsync(message);
            await Task.Yield();
            Assert.Equal("captured", ambient.Value);
        }
        Assert.Equal("parent", ambient.Value);
        using (scope.Enter()) await scope.RestoreAsync(message);
        await scope.DisposeAsync();
        Assert.Equal("parent", ambient.Value);
        Assert.Equal(new[] { "captured", "captured" }, restored);
        Assert.Equal(2, leases.Count);
        foreach (var lease in leases) lease.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Failed_Restore_Disposes_Resolved_Services_While_The_Entered_Lease_Is_Active() {
        var ambient = new AsyncLocal<bool>();
        var disposable = new Mock<IDisposable>();
        disposable.Setup(value => value.Dispose()).Callback(() => Assert.True(ambient.Value));
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> _, IServiceProvider target, CancellationToken _) => {
                target.GetRequiredService<IDisposable>();
                return ValueTask.FromException(new InvalidOperationException("restore"));
            });
        using var root = new ServiceCollection().AddScoped(_ => disposable.Object).AddSingleton(propagator.Object).BuildServiceProvider();
        var lease = new Mock<IDisposable>();
        lease.Setup(value => value.Dispose()).Callback(() => ambient.Value = false);
        var scope = new MessageExecutionScope(root.CreateAsyncScope(), () => { ambient.Value = true; return lease.Object; });
        using (scope.Enter()) {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => {
                await using var owned = scope;
                await scope.RestoreAsync(new(new Dictionary<string, string?>()));
            });
        }
        Assert.False(ambient.Value);
        disposable.Verify(value => value.Dispose(), Times.Once);
        lease.Verify(value => value.Dispose(), Times.Once);
    }
}

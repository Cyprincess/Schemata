using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

[Trait("Layer", "Unit")]
public class MessageExecutionScopeShould
{
    [Fact]
    public async Task RunAsync_Returns_Work_Result_After_Create_Restore_And_Dispose() {
        var events = new List<string>();
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("restore")).Returns(ValueTask.CompletedTask);
        using var provider = new ServiceCollection().AddSingleton(propagator.Object)
            .AddScoped<DisposalProbe>().AddSingleton(events).BuildServiceProvider();
        var scopes = new MessageExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var factory = new Mock<IMessageExecutionScopeFactory>();
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("create"))
            .Returns<MessageContext?, CancellationToken>((context, ct) => scopes.CreateAsync(context, ct));
        var runner = new MessageExecutionScopeRunner(factory.Object);
        var context = new MessageContext(new Dictionary<string, string?>());

        var result = await runner.RunAsync(context, (services, _) => {
            services.GetRequiredService<DisposalProbe>();
            events.Add("work");
            return ValueTask.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(["create", "restore", "work", "dispose"], events);
    }

    [Fact]
    public async Task RunAsync_Creation_Failure_Does_Not_Invoke_Work() {
        var failure = new InvalidOperationException("create");
        var factory = new Mock<IMessageExecutionScopeFactory>();
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromException<MessageExecutionScope>(failure));
        var runner = new MessageExecutionScopeRunner(factory.Object);
        var invoked = false;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync<int>(null, (_, _) => {
            invoked = true;
            return ValueTask.FromResult(1);
        }).AsTask());

        Assert.Same(failure, actual);
        Assert.False(invoked);
    }

    [Fact]
    public async Task RunAsync_Restore_Failure_Disposes_Created_Scope_And_Rethrows_Single_Failure() {
        var events = new List<string>();
        var failure = new InvalidOperationException("restore");
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("restore")).Returns(ValueTask.FromException(failure));
        var provider = new ServiceCollection().AddSingleton(propagator.Object).BuildServiceProvider();
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(value => value.ServiceProvider).Returns(provider);
        scope.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Callback(() => events.Add("dispose")).Returns(ValueTask.CompletedTask);
        var factory = new Mock<IMessageExecutionScopeFactory>();
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("create"))
            .Returns(ValueTask.FromResult(new MessageExecutionScope(new AsyncServiceScope(scope.Object))));
        var runner = new MessageExecutionScopeRunner(factory.Object);
        var invoked = false;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync<int>(new(new Dictionary<string, string?>()), (_, _) => {
            invoked = true;
            return ValueTask.FromResult(1);
        }).AsTask());

        Assert.Same(failure, actual);
        Assert.False(invoked);
        Assert.Equal(["create", "restore", "dispose"], events);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_Aggregates_Work_Failure_Before_Cleanup_Failure() {
        var body = new InvalidOperationException("work");
        var cleanup = new ApplicationException("cleanup");
        var events = new List<string>();
        var provider = new ServiceCollection().BuildServiceProvider();
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(value => value.ServiceProvider).Returns(provider);
        scope.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Callback(() => events.Add("dispose")).Returns(ValueTask.FromException(cleanup));
        var factory = new Mock<IMessageExecutionScopeFactory>();
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext?>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("create"))
            .Returns(ValueTask.FromResult(new MessageExecutionScope(new AsyncServiceScope(scope.Object))));
        var runner = new MessageExecutionScopeRunner(factory.Object);

        var actual = await Assert.ThrowsAsync<AggregateException>(() => runner.RunAsync<int>(null, (_, _) => {
            events.Add("work");
            return ValueTask.FromException<int>(body);
        }).AsTask());

        Assert.Equal(new Exception[] { body, cleanup }, actual.InnerExceptions);
        Assert.Equal(["create", "work", "dispose"], events);
        await provider.DisposeAsync();
    }

    private sealed class DisposalProbe(List<string> events) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}

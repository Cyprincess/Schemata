using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

[Trait("Layer", "Integration")]
public sealed class StreamDispatcherShould
{
    [Fact]
    public async Task Capture_Context_And_Isolate_Concurrent_Enumerations_Through_Early_Disposal() {
        var captured = Guid.NewGuid();
        var disposed = new List<Guid>();
        var services = new ServiceCollection();
        services.AddSingleton(new TestIdentity(captured));
        services.AddScoped(_ => new Lease(disposed));
        services.AddScoped<IStreamRequestHandler<Request, Observation>, Handler>();
        services.AddSchemataStreams();
        services.AddSingleton<IMessageContextPropagator, ContextPropagator>();
        var factory = new Mock<IMessageExecutionScopeFactory>();
        IServiceProvider? root = null;
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext, CancellationToken>((_, _) => new(new MessageExecutionScope(root!.CreateAsyncScope())));
        services.AddSingleton(factory.Object);
        using var provider = services.BuildServiceProvider();
        root = provider;
        var identity = new ClaimsIdentity([new Claim("sub", "users/original")], "test");
        var stream = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new(), new ClaimsPrincipal(identity));
        factory.Verify(value => value.CreateAsync(It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()), Times.Never);
        identity.RemoveClaim(identity.FindFirst("sub")!);
        identity.AddClaim(new("sub", "users/changed"));
        await using var first = stream.GetAsyncEnumerator();
        await using var second = stream.GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(captured, first.Current.Context);
        Assert.Equal("users/original", first.Current.Subject);
        Assert.NotEqual(first.Current.Scope, second.Current.Scope);
        Assert.NotSame(first.Current.Advice, second.Current.Advice);
        Assert.True(await first.MoveNextAsync());
        Assert.Equal(captured, first.Current.Context);
        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal(2, disposed.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Release_Scope_After_Completion_Or_Handler_Exception(bool fail) {
        var disposed = new List<Guid>();
        using var provider = new ServiceCollection().AddSingleton(new TestIdentity(Guid.NewGuid()))
            .AddScoped(_ => new Lease(disposed))
            .AddScoped<IStreamRequestHandler<Request, Observation>, Handler>().AddSchemataStreams().BuildServiceProvider();
        var stream = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new(fail));
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(async () => { await foreach (var _ in stream) { } });
        else { await foreach (var item in stream) Assert.NotEqual(Guid.Empty, item.Scope); }
        Assert.Single(disposed);
    }

    [Fact]
    public async Task Release_Active_Scope_When_Enumeration_Is_Canceled() {
        var disposed = new List<Guid>();
        using var provider = new ServiceCollection().AddSingleton(new TestIdentity(Guid.NewGuid())).AddScoped(_ => new Lease(disposed))
            .AddScoped<IStreamRequestHandler<Request, Observation>, Handler>().AddSchemataStreams().BuildServiceProvider();
        using var canceled = new CancellationTokenSource();
        await using var enumerator = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new()).GetAsyncEnumerator(canceled.Token);
        Assert.True(await enumerator.MoveNextAsync());
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enumerator.MoveNextAsync().AsTask());
        Assert.Single(disposed);
        await enumerator.DisposeAsync();
        Assert.Single(disposed);
    }

    [Fact]
    public async Task Preserve_Handler_And_Disposal_Failures() {
        var enumerator = new Mock<IAsyncEnumerator<Observation>>();
        enumerator.Setup(value => value.MoveNextAsync()).ThrowsAsync(new InvalidOperationException("handler"));
        enumerator.Setup(value => value.DisposeAsync()).ThrowsAsync(new ApplicationException("cleanup"));
        var enumerable = new Mock<IAsyncEnumerable<Observation>>();
        enumerable.Setup(value => value.GetAsyncEnumerator(It.IsAny<CancellationToken>())).Returns(enumerator.Object);
        var handler = new Mock<IStreamRequestHandler<Request, Observation>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<Request>(), It.IsAny<StreamExecutionContext>(), It.IsAny<CancellationToken>())).Returns(enumerable.Object);
        using var provider = new ServiceCollection().AddSingleton(handler.Object).AddSchemataStreams().BuildServiceProvider();
        await using var actual = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new()).GetAsyncEnumerator();
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual.MoveNextAsync().AsTask());
        Assert.Equal(new[] { "handler", "cleanup" }, error.Flatten().InnerExceptions.Select(value => value.Message));
        enumerator.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Evaluate_Current_Under_Captured_Context() {
        var captured = Guid.NewGuid();
        var enumerator = new Mock<IAsyncEnumerator<Observation>>();
        var disposed = new List<Guid>();
        enumerator.Setup(value => value.DisposeAsync()).Callback(() => disposed.Add(captured)).Returns(ValueTask.CompletedTask);
        enumerator.SetupSequence(value => value.MoveNextAsync()).ReturnsAsync(true).ReturnsAsync(false);
        enumerator.SetupGet(value => value.Current).Returns(() => new(Guid.Empty, captured, null, null!));
        var enumerable = new Mock<IAsyncEnumerable<Observation>>();
        enumerable.Setup(value => value.GetAsyncEnumerator(It.IsAny<CancellationToken>())).Returns(enumerator.Object);
        var handler = new Mock<IStreamRequestHandler<Request, Observation>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<Request>(), It.IsAny<StreamExecutionContext>(), It.IsAny<CancellationToken>())).Returns(enumerable.Object);
        var factory = new Mock<IMessageExecutionScopeFactory>();
        IServiceProvider? root = null;
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext, CancellationToken>((_, _) => new(new MessageExecutionScope(root!.CreateAsyncScope())));
        using var provider = new ServiceCollection().AddSingleton(handler.Object).AddSingleton(factory.Object)
            .AddSingleton(new TestIdentity(captured)).AddSingleton<IMessageContextPropagator, ContextPropagator>()
            .AddSchemataStreams().BuildServiceProvider();
        root = provider;
        var stream = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new());
        await using var actual = stream.GetAsyncEnumerator();
        Assert.True(await actual.MoveNextAsync());
        Assert.Equal(captured, actual.Current.Context);
        await actual.DisposeAsync();
        Assert.Equal(new[] { captured }, disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Restore_Releases_The_Created_Scope_Without_Restoring_Again(bool cleanupFails) {
        var failure = new InvalidOperationException("restore");
        var cleanup = new ApplicationException("scope cleanup");
        var disposal = new Mock<IDisposable>();
        if (cleanupFails) disposal.Setup(value => value.Dispose()).Throws(cleanup);
        var propagator = new Mock<IMessageContextPropagator>();
        var restores = 0;
        propagator.Setup(value => value.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Callback(() => restores++).Returns(ValueTask.FromException(failure));
        var factory = new Mock<IMessageExecutionScopeFactory>();
        IServiceProvider? root = null;
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext, CancellationToken>((_, _) => {
                var scope = root!.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IDisposable>();
                return new(new MessageExecutionScope(scope));
            });
        using var provider = new ServiceCollection().AddScoped(_ => disposal.Object).AddSingleton(factory.Object)
            .AddSingleton(propagator.Object).AddSchemataStreams().BuildServiceProvider();
        root = provider;
        await using var actual = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new()).GetAsyncEnumerator();
        if (cleanupFails) {
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual.MoveNextAsync().AsTask());
            Assert.Equal(new Exception[] { failure, cleanup }, error.Flatten().InnerExceptions);
        } else {
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => actual.MoveNextAsync().AsTask()));
        }
        await actual.DisposeAsync();
        Assert.Equal(1, restores);
        disposal.Verify(value => value.Dispose(), Times.Once);
    }

    public sealed record TestIdentity(Guid Value);

    public sealed class ContextPropagator : IMessageContextPropagator
    {
        public void Capture(IDictionary<string, string?> items, IServiceProvider source) =>
            items["test.context"] = source.GetRequiredService<TestIdentity>().Value.ToString("D");
        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
            Assert.Equal(Guid.Parse(items["test.context"]!), target.GetRequiredService<TestIdentity>().Value);
            return ValueTask.CompletedTask;
        }
    }
    public sealed record Request(bool Fail = false) : IStreamRequest<Observation>;
    public sealed record Observation(Guid Scope, Guid? Context, string? Subject, AdviceContext Advice);
    public sealed class Lease(List<Guid> disposed) : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public void Dispose() => disposed.Add(Id);
    }
    public sealed class Handler(Lease lease, TestIdentity identity) : IStreamRequestHandler<Request, Observation>
    {
        public async IAsyncEnumerable<Observation> HandleAsync(Request request, StreamExecutionContext context, [EnumeratorCancellation] CancellationToken ct = default) {
            yield return Observe();
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            if (request.Fail) throw new InvalidOperationException("stream failed");
            yield return Observe();
            Observation Observe() => new(lease.Id, identity.Value, context.Principal?.FindFirst("sub")?.Value, context.Advice);
        }
    }
}

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
using Schemata.Abstractions.Tenancy;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

public sealed class StreamDispatcherShould
{
    [Fact]
    public async Task Capture_Identity_And_Isolate_Concurrent_Enumerations_Through_Early_Disposal() {
        var disposed = new List<Guid>();
        var services = new ServiceCollection();
        services.AddScoped(_ => new Lease(disposed));
        services.AddScoped<IStreamRequestHandler<Request, Observation>, Handler>();
        services.AddSchemataStreams();
        var factory = new Mock<IMessageExecutionScopeFactory>();
        IServiceProvider? root = null;
        factory.Setup(value => value.CreateAsync(It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()))
            .Returns<MessageContext, CancellationToken>((context, _) => new(new MessageExecutionScope(root!.CreateAsyncScope(), MessageContexts.Identity(context))));
        services.AddSingleton(factory.Object);
        using var provider = services.BuildServiceProvider();
        root = provider;
        var tenant = Guid.NewGuid();
        var identity = new ClaimsIdentity([new Claim("sub", "users/original")], "test");
        IAsyncEnumerable<Observation> stream;
        using (TenantContext.Enter(new(tenant))) {
            stream = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new(), new ClaimsPrincipal(identity));
        }
        factory.Verify(value => value.CreateAsync(It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()), Times.Never);
        identity.RemoveClaim(identity.FindFirst("sub")!);
        identity.AddClaim(new("sub", "users/changed"));
        await using var first = stream.GetAsyncEnumerator();
        await using var second = stream.GetAsyncEnumerator();
        var caller = Guid.NewGuid();
        using (TenantContext.Enter(new(caller))) {
            Assert.True(await first.MoveNextAsync());
            Assert.True(await second.MoveNextAsync());
            Assert.Equal(caller, TenantContext.Current.Uid);
            Assert.Equal(tenant, first.Current.Tenant);
            Assert.Equal("users/original", first.Current.Subject);
            Assert.NotEqual(first.Current.Scope, second.Current.Scope);
            Assert.NotSame(first.Current.Advice, second.Current.Advice);
            Assert.True(await first.MoveNextAsync());
            Assert.Equal(tenant, first.Current.Tenant);
            await first.DisposeAsync();
            await second.DisposeAsync();
            Assert.Equal(caller, TenantContext.Current.Uid);
        }
        Assert.Equal(2, disposed.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Release_Scope_After_Completion_Or_Handler_Exception(bool fail) {
        var disposed = new List<Guid>();
        using var provider = new ServiceCollection().AddScoped(_ => new Lease(disposed))
            .AddScoped<IStreamRequestHandler<Request, Observation>, Handler>().AddSchemataStreams().BuildServiceProvider();
        var stream = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new(fail));
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(async () => { await foreach (var _ in stream) { } });
        else { await foreach (var item in stream) Assert.NotEqual(Guid.Empty, item.Scope); }
        Assert.Single(disposed);
    }

    [Fact]
    public async Task Release_Active_Scope_When_Enumeration_Is_Canceled() {
        var disposed = new List<Guid>();
        using var provider = new ServiceCollection().AddScoped(_ => new Lease(disposed))
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
    public async Task Evaluate_Current_Under_Captured_Identity_Not_Consumer_Frame() {
        var enumerator = new Mock<IAsyncEnumerator<Observation>>();
        enumerator.SetupSequence(value => value.MoveNextAsync()).ReturnsAsync(true).ReturnsAsync(false);
        enumerator.SetupGet(value => value.Current).Returns(() => new(Guid.Empty, TenantContext.Current.Uid, null, null!));
        var enumerable = new Mock<IAsyncEnumerable<Observation>>();
        enumerable.Setup(value => value.GetAsyncEnumerator(It.IsAny<CancellationToken>())).Returns(enumerator.Object);
        var handler = new Mock<IStreamRequestHandler<Request, Observation>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<Request>(), It.IsAny<StreamExecutionContext>(), It.IsAny<CancellationToken>())).Returns(enumerable.Object);
        using var provider = new ServiceCollection().AddSingleton(handler.Object).AddSchemataStreams().BuildServiceProvider();
        var stream = provider.GetRequiredService<IStreamDispatcher>().Stream<Request, Observation>(new());
        using var caller = TenantContext.Enter(new(Guid.NewGuid()));
        await using var actual = stream.GetAsyncEnumerator();
        Assert.True(await actual.MoveNextAsync());
        Assert.Null(actual.Current.Tenant);
        Assert.NotNull(TenantContext.Current.Uid);
    }

    public sealed record Request(bool Fail = false) : IStreamRequest<Observation>;
    public sealed record Observation(Guid Scope, Guid? Tenant, string? Subject, AdviceContext Advice);
    public sealed class Lease(List<Guid> disposed) : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public void Dispose() => disposed.Add(Id);
    }
    public sealed class Handler(Lease lease) : IStreamRequestHandler<Request, Observation>
    {
        public async IAsyncEnumerable<Observation> HandleAsync(Request request, StreamExecutionContext context, [EnumeratorCancellation] CancellationToken ct = default) {
            yield return Observe();
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            if (request.Fail) throw new InvalidOperationException("stream failed");
            yield return Observe();
            Observation Observe() => new(lease.Id, TenantContext.Current.Uid, context.Principal?.FindFirst("sub")?.Value, context.Advice);
        }
    }
}

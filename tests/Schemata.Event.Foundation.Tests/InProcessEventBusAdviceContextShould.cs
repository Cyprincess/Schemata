using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>
///     Asserts <see cref="InProcessEventBus" /> hands the publish and consume advisors explicit
///     <see cref="AdviceContext" /> instances bound to the dispatch's own scope.
/// </summary>
public class InProcessEventBusAdviceContextShould
{
    [Fact]
    public async Task Publish_RunsBothAdvisorStages_OnTheDispatchScopeContext() {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns("sample");
        registry.Setup(r => r.GetRouting(It.IsAny<Type>())).Returns(EventRouting.Broadcast);

        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                          It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                     .Returns(EmptyAsync<SchemataEventSubscription>());

        var publish = new RecordingPublishAdvisor();
        var consume = new RecordingConsumeAdvisor();
        var handler = new CountingHandler();

        await using var services = new ServiceCollection()
                                   .AddSingleton(registry.Object)
                                   .AddSingleton(subscriptions.Object)
                                   .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
                                   .AddSingleton<HandlerResolver>()
                                   .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                   .AddSingleton<IEventHandler<SampleEvent>>(handler)
                                   .AddSingleton<IEventPublishAdvisor>(publish)
                                   .AddSingleton<IEventConsumeAdvisor>(consume)
                                   .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new SampleEvent());

        Assert.Equal(1, handler.Calls);
        Assert.NotNull(publish.Observed);
        Assert.NotNull(consume.Observed);
        Assert.Same(publish.Observed.ServiceProvider, consume.Observed.ServiceProvider);
        Assert.NotSame(services, publish.Observed.ServiceProvider);
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        yield break;
    }

    public sealed class SampleEvent : IEvent;

    private sealed class CountingHandler : IEventHandler<SampleEvent>
    {
        public int Calls { get; private set; }

        public Task HandleAsync(SampleEvent @event, CancellationToken ct = default) {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublishAdvisor : IEventPublishAdvisor
    {
        public AdviceContext? Observed { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, EventContext a1, CancellationToken ct = default) {
            Observed = ctx;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class RecordingConsumeAdvisor : IEventConsumeAdvisor
    {
        public AdviceContext? Observed { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, EventContext a1, CancellationToken ct = default) {
            Observed = ctx;
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}

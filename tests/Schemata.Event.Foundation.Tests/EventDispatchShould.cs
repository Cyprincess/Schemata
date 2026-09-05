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
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>Direct-dispatch semantics of <see cref="InProcessEventBus" />.</summary>
public class EventDispatchShould
{
    private const string EventName = "sample-event";

    [Fact]
    public async Task Publish_Handler_Succeeds_Audit_Row_Transitions_Recorded_To_Succeeded_With_ResponsePayload() {
        var addedStates = new List<EventState>();
        var updated     = new List<SchemataEvent>();
        var records     = RecordsMock(addedStates, updated);

        var handler = new Mock<IEventHandler<SampleEvent>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        await using var services = Base(records)
            .AddSingleton(handler.Object)
            .BuildServiceProvider();
        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new SampleEvent());

        Assert.Equal(EventState.Recorded, Assert.Single(addedStates));
        var row = Assert.Single(updated);
        Assert.Equal(EventState.Succeeded, row.State);
        Assert.Equal("true", row.ResponsePayload);
        handler.Verify(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_Handler_Throws_Rethrows_And_Audit_Row_Records_Failed_With_RecentError() {
        var addedStates = new List<EventState>();
        var updated     = new List<SchemataEvent>();
        var records     = RecordsMock(addedStates, updated);

        var handler = new Mock<IEventHandler<SampleEvent>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("handler failed"));

        await using var services = Base(records)
            .AddSingleton(handler.Object)
            .BuildServiceProvider();
        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new SampleEvent()));

        Assert.Equal("handler failed", ex.Message);
        Assert.Equal(EventState.Recorded, Assert.Single(addedStates));
        var row = Assert.Single(updated);
        Assert.Equal(EventState.Failed, row.State);
        Assert.Equal("handler failed", row.RecentError);
    }

    [Fact]
    public async Task Publish_Advisor_Blocks_Throws_No_Audit_Row_No_Handler_Invocation() {
        var addedStates = new List<EventState>();
        var updated     = new List<SchemataEvent>();
        var records     = RecordsMock(addedStates, updated);

        var handler = new Mock<IEventHandler<SampleEvent>>();

        var advisor = new Mock<IEventPublishAdvisor>();
        advisor.Setup(a => a.AdviseAsync(
                       It.IsAny<AdviceContext>(),
                       It.IsAny<EventContext>(),
                       It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Block);

        await using var services = Base(records)
            .AddSingleton<IEventPublishAdvisor>(advisor.Object)
            .AddSingleton(handler.Object)
            .BuildServiceProvider();
        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new SampleEvent()));

        Assert.Equal("Event publish blocked by advisor.", ex.Message);
        Assert.Empty(addedStates);
        Assert.Empty(updated);
        handler.Verify(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_Observers_Run_Application_Observers_Before_Audit_Observer() {
        var addedStates = new List<EventState>();
        var updated     = new List<SchemataEvent>();
        var calls       = new List<string>();
        // The audit observer's OnConsumedAsync commits through UpdateAsync.
        var records = RecordsMock(addedStates, updated, _ => calls.Add("audit"));

        var application = new Mock<IEventLifecycleObserver>();
        application.Setup(o => o.OnConsumedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                   .Callback(() => calls.Add("app"))
                   .Returns(Task.CompletedTask);

        await using var services = Base(records)
            .AddSingleton(application.Object)
            .AddSingleton(Mock.Of<IEventHandler<SampleEvent>>())
            .BuildServiceProvider();
        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new SampleEvent());

        // The audit observer is registered before the application observer in DI; the enforced
        // audit-last consume order still runs the application observer first.
        Assert.Collection(calls, c => Assert.Equal("app", c), c => Assert.Equal("audit", c));
    }

    private static Mock<IRepository<SchemataEvent>> RecordsMock(
        List<EventState>       addedStates,
        List<SchemataEvent>    updated,
        Action<SchemataEvent>? onUpdate = null
    ) {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEvent row, CancellationToken _) => addedStates.Add(row.State))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEvent row, CancellationToken _) => {
                   updated.Add(row);
                   onUpdate?.Invoke(row);
               })
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return records;
    }

    private static IServiceCollection Base(Mock<IRepository<SchemataEvent>> records) {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns(EventName);
        registry.Setup(r => r.GetRouting(It.IsAny<Type>())).Returns(EventRouting.Broadcast);

        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                          It.IsAny<CancellationToken>()))
                     .Returns(EmptyAsync<SchemataEventSubscription>());

        return new ServiceCollection()
               .AddSingleton(registry.Object)
               .AddSingleton(subscriptions.Object)
               .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
               .AddSingleton<HandlerResolver>()
               .AddSingleton(records.Object)
               .AddSingleton<IEventLifecycleObserver>(new SchemataEventAuditObserver(
                   records.Object, Options.Create(new JsonSerializerOptions())));
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        yield break;
    }

    public sealed class SampleEvent : IEvent;
}

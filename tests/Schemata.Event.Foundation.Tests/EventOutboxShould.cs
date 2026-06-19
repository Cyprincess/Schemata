using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class EventOutboxShould
{
    [Fact]
    public async Task PublishFails_RowStaysPending() {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var observer = new SchemataEventAuditObserver(records.Object, Options.Create(new JsonSerializerOptions()));
        var context = new EventContext(new SampleEvent(), "sample") {
            Payload = "{}", CorrelationId = "c1", RequiresOutboxDelivery = true,
        };

        // OnPublished records the outbox row; broker delivery controls the terminal callback.
        await observer.OnPublishedAsync(context);

        Assert.NotNull(context.Record);
        Assert.Equal(EventState.Pending, context.Record!.State);
    }

    [Fact]
    public async Task PendingRetried_RetryCountIncrements() {
        var record = new SchemataEvent {
            EventType     = "sample",
            Payload       = "{}",
            CorrelationId = "c1",
            State         = EventState.Pending,
        };
        var dispatcher = Dispatcher(record, new ThrowingPublisher());

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(EventState.Pending, record.State);
        Assert.Equal(1, record.RetryCount);
        Assert.Equal("publish boom", record.RecentError);
    }

    [Fact]
    public async Task PendingPublished_MarksRecorded() {
        var record = new SchemataEvent {
            EventType     = "sample",
            Payload       = "{}",
            CorrelationId = "c1",
            State         = EventState.Pending,
        };
        var publisher  = new RecordingPublisher();
        var dispatcher = Dispatcher(record, publisher);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(EventState.Recorded, record.State);
        Assert.Equal("sample", publisher.Published?.EventType);
    }

    [Fact]
    public async Task DispatchPendingAsync_DrainsInProcessOutboxWithSourceSnapshot() {
        var json     = new JsonSerializerOptions();
        var rows     = new List<SchemataEvent>();
        var records  = Repository(rows);
        var registry = new DefaultEventTypeRegistry();
        registry.Register(typeof(SampleEvent), "sample");

        var handler  = new CountingHandler();
        var observer = new CapturingObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IRepository<SchemataEvent>>(records.Object);
        services.AddSingleton<IEventTypeRegistry>(registry);
        services.AddSingleton<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        services.AddScoped<HandlerResolver>();
        services.AddScoped<IEventDispatchContext, EventDispatchContext>();
        services.AddSingleton(Options.Create(new SchemataEventOptions()));
        services.AddSingleton<IEventHandler<SampleEvent>>(handler);
        services.AddSingleton<IEventLifecycleObserver>(sp => new SchemataEventAuditObserver(
                                                           sp.GetRequiredService<IRepository<SchemataEvent>>(),
                                                           Options.Create(json)));
        services.AddSingleton<IEventLifecycleObserver>(observer);
        var sp = services.BuildServiceProvider();

        var publisher  = new InProcessEventOutboxPublisher(sp, Options.Create(json));
        var dispatcher = new EventOutboxDispatcher(sp, publisher);
        var bus        = new InProcessEventBus(sp, Options.Create(json), dispatcher: dispatcher);
        var source     = new SourceEntity { CanonicalName = "sources/1", Timestamp = Identifiers.NewUid() };

        await bus.PublishAsync(new SampleEvent { Value = "payload" }, source);

        Assert.Single(rows);
        Assert.Equal(EventState.Pending, rows[0].State);
        Assert.Equal(0, handler.Count);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(1, handler.Count);

        // In-process replay consumes synchronously; the audit observer commits the terminal state,
        // and the dispatcher preserves it.
        Assert.Equal(EventState.Succeeded, rows[0].State);
        var snapshot  = Assert.IsAssignableFrom<ICanonicalName>(observer.Consumed?.Source);
        var stamped   = Assert.IsAssignableFrom<IConcurrency>(observer.Consumed?.Source);
        var reference = Assert.IsAssignableFrom<ISourceReference>(observer.Consumed?.Source);
        Assert.Equal("sources/1", snapshot.CanonicalName);
        Assert.Equal(source.Timestamp, stamped.Timestamp);
        Assert.Equal(typeof(SourceEntity).FullName, reference.SourceType);
        Assert.Equal(source.Timestamp, reference.SourceTimestamp);
    }

    [Fact]
    public async Task DispatchPendingAsync_InvokesOnDeliveredAsyncOnCustomObserver() {
        var json = new JsonSerializerOptions();
        var rows = new List<SchemataEvent> {
            new() {
                EventType     = "sample",
                Payload       = JsonSerializer.Serialize(new SampleEvent { Value = "x" }, json),
                CorrelationId = "delivery-correlation",
                State         = EventState.Pending,
            },
        };
        var records  = Repository(rows);
        var registry = new DefaultEventTypeRegistry();
        registry.Register(typeof(SampleEvent), "sample");

        var delivered = new DeliveryRecordingObserver();
        var services  = new ServiceCollection();
        services.AddSingleton<IRepository<SchemataEvent>>(records.Object);
        services.AddSingleton<IEventTypeRegistry>(registry);
        services.AddSingleton<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        services.AddScoped<HandlerResolver>();
        services.AddScoped<IEventDispatchContext, EventDispatchContext>();
        services.AddSingleton(Options.Create(new SchemataEventOptions()));
        services.AddSingleton<IEventHandler<SampleEvent>>(new CountingHandler());
        services.AddSingleton<IEventLifecycleObserver>(delivered);
        var sp = services.BuildServiceProvider();

        var publisher  = new InProcessEventOutboxPublisher(sp, Options.Create(json));
        var dispatcher = new EventOutboxDispatcher(sp, publisher);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(1, delivered.DeliveredCount);
        Assert.Equal("sample", delivered.LastDelivered?.EventType);
        Assert.Equal("delivery-correlation", delivered.LastDelivered?.CorrelationId);
    }

    private static EventOutboxDispatcher Dispatcher(SchemataEvent record, IEventOutboxPublisher publisher) {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>?>(),
                          It.IsAny<CancellationToken>()))
               .Returns((Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>? predicate, CancellationToken _)
                            => ToAsync(predicate!(new[] { record }.AsQueryable())));
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(records.Object).BuildServiceProvider();
        return new(services, publisher);
    }

    private static Mock<IRepository<SchemataEvent>> Repository(List<SchemataEvent> rows) {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEvent row, CancellationToken _) => rows.Add(row))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>?>(),
                                       It.IsAny<CancellationToken>()))
               .Returns((Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>? predicate, CancellationToken _)
                            => ToAsync(predicate!(rows.AsQueryable())));
        records.Setup(r => r.FirstOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>?>(),
                          It.IsAny<CancellationToken>()))
               .Returns((Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>? predicate, CancellationToken _)
                            => ValueTask.FromResult(predicate!(rows.AsQueryable()).FirstOrDefault()));
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return records;
    }

    private static async IAsyncEnumerable<SchemataEvent> ToAsync(IEnumerable<SchemataEvent> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    #region Nested type: CapturingObserver

    private sealed class CapturingObserver : IEventLifecycleObserver
    {
        public EventContext? Consumed { get; private set; }

        #region IEventLifecycleObserver Members

        public Task OnPublishedAsync(EventContext context, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task OnConsumedAsync(EventContext context, CancellationToken ct = default) {
            Consumed = context;
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion

    #region Nested type: CountingHandler

    private sealed class CountingHandler : IEventHandler<SampleEvent>
    {
        public int Count { get; private set; }

        #region IEventHandler<SampleEvent> Members

        public Task HandleAsync(SampleEvent @event, CancellationToken ct = default) {
            Count++;
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion

    #region Nested type: DeliveryRecordingObserver

    private sealed class DeliveryRecordingObserver : IEventLifecycleObserver
    {
        public int           DeliveredCount { get; private set; }
        public EventContext? LastDelivered  { get; private set; }

        #region IEventLifecycleObserver Members

        public Task OnPublishedAsync(EventContext context, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task OnDeliveredAsync(EventContext context, CancellationToken ct = default) {
            DeliveredCount++;
            LastDelivered = context;
            return Task.CompletedTask;
        }

        public Task OnConsumedAsync(EventContext context, CancellationToken ct = default) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: RecordingPublisher

    private sealed class RecordingPublisher : IEventOutboxPublisher
    {
        public EventOutboxMessage? Published { get; private set; }

        #region IEventOutboxPublisher Members

        public Task<EventOutboxDelivery> PublishAsync(EventOutboxMessage message, CancellationToken ct = default) {
            Published = message;
            return Task.FromResult(EventOutboxDelivery.Delivered);
        }

        #endregion
    }

    #endregion

    #region Nested type: SampleEvent

    private sealed class SampleEvent : IEvent
    {
        public string? Value { get; set; }
    }

    #endregion

    #region Nested type: SourceEntity

    private sealed class SourceEntity : ICanonicalName, IConcurrency
    {
        #region ICanonicalName Members

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        #endregion

        #region IConcurrency Members

        public Guid Timestamp { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: ThrowingPublisher

    private sealed class ThrowingPublisher : IEventOutboxPublisher
    {
        #region IEventOutboxPublisher Members

        public Task<EventOutboxDelivery> PublishAsync(EventOutboxMessage message, CancellationToken ct = default) {
            throw new InvalidOperationException("publish boom");
        }

        #endregion
    }

    #endregion
}

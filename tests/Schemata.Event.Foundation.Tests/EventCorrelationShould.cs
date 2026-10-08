using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>
///     Subscription-side correlation filtering of <see cref="InProcessEventBus" />: the envelope's
///     business correlation metadata decides which armed subscription rows reach the handlers.
/// </summary>
public class EventCorrelationShould
{
    private const string EventName = "sample-event";

    [Fact]
    public async Task Publish_With_Correlation_Delivers_Only_To_Matching_Filtered_Subscriptions() {
        var rows = new[] {
            new SchemataEventSubscription {
                SubscriptionId   = "sub-hit",
                EventType        = EventName,
                Target           = "targets/hit",
                CorrelationFilter = new() { ["order"] = "o-1" },
            },
            new SchemataEventSubscription {
                SubscriptionId   = "sub-miss",
                EventType        = EventName,
                Target           = "targets/miss",
                CorrelationFilter = new() { ["order"] = "o-2" },
            },
            new SchemataEventSubscription {
                SubscriptionId = "sub-all",
                EventType      = EventName,
                Target         = "targets/all",
            },
        };

        var (bus, dispatch) = BuildBus(rows);

        await bus.PublishAsync(new SampleEvent(), new Dictionary<string, string> { ["order"] = "o-1" });

        var matched = dispatch.MatchedSubscriptions ?? [];
        Assert.Equal(2, matched.Count);
        Assert.Contains(matched, row => row.SubscriptionId == "sub-hit");
        Assert.Contains(matched, row => row.SubscriptionId == "sub-all");
    }

    [Fact]
    public async Task Publish_Without_Correlation_Delivers_Only_To_Unfiltered_Subscriptions() {
        var rows = new[] {
            new SchemataEventSubscription {
                SubscriptionId   = "sub-filtered",
                EventType        = EventName,
                Target           = "targets/filtered",
                CorrelationFilter = new() { ["order"] = "o-1" },
            },
            new SchemataEventSubscription {
                SubscriptionId = "sub-all",
                EventType      = EventName,
                Target         = "targets/all",
            },
        };

        var (bus, dispatch) = BuildBus(rows);

        await bus.PublishAsync(new SampleEvent());

        var matched = dispatch.MatchedSubscriptions ?? [];
        var row     = Assert.Single(matched);
        Assert.Equal("sub-all", row.SubscriptionId);
    }

    [Fact]
    public async Task Publish_Requires_Every_Filter_Pair_To_Match() {
        var rows = new[] {
            new SchemataEventSubscription {
                SubscriptionId   = "sub-pair",
                EventType        = EventName,
                Target           = "targets/pair",
                CorrelationFilter = new() { ["order"] = "o-1", ["case"] = "c-9" },
            },
        };

        var (bus, dispatch) = BuildBus(rows);

        await bus.PublishAsync(new SampleEvent(), new Dictionary<string, string> { ["order"] = "o-1" });
        Assert.Empty(dispatch.MatchedSubscriptions ?? []);

        await bus.PublishAsync(
            new SampleEvent(),
            new Dictionary<string, string> { ["order"] = "o-1", ["case"] = "c-9", ["extra"] = "x" });
        var matched = Assert.Single(dispatch.MatchedSubscriptions ?? []);
        Assert.Equal("sub-pair", matched.SubscriptionId);
    }

    [Fact]
    public async Task Publish_With_Correlation_Compares_Filter_Values_Ordinally() {
        var rows = new[] {
            new SchemataEventSubscription {
                SubscriptionId   = "sub-case",
                EventType        = EventName,
                Target           = "targets/case",
                CorrelationFilter = new() { ["order"] = "o-1" },
            },
        };

        var (bus, dispatch) = BuildBus(rows);

        await bus.PublishAsync(new SampleEvent(), new Dictionary<string, string> { ["order"] = "O-1" });

        Assert.Empty(dispatch.MatchedSubscriptions ?? []);
    }

    private static (InProcessEventBus Bus, EventDispatchContext Dispatch) BuildBus(
        IReadOnlyList<SchemataEventSubscription> rows
    ) {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns(EventName);
        registry.Setup(r => r.GetRouting(It.IsAny<Type>())).Returns(EventRouting.Broadcast);

        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                          It.IsAny<CancellationToken>()))
                     .Returns((Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>> query,
                               CancellationToken _) => Stream(query(rows.AsQueryable())));

        var dispatch = new EventDispatchContext();

        // The catch-all handler observes the matched set the same way bridge handlers do.
        var handler = new Mock<IEventHandler<IEvent>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var services = new ServiceCollection()
                      .AddSingleton(registry.Object)
                      .AddSingleton(subscriptions.Object)
                      .AddSingleton<IEventDispatchContext>(dispatch)
                      .AddSingleton<HandlerResolver>()
                      .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                      .AddSingleton(handler.Object)
                      .BuildServiceProvider();

        return (new(services, Options.Create(new JsonSerializerOptions())), dispatch);
    }

    private static async IAsyncEnumerable<T> Stream<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }

    public sealed class SampleEvent : IEvent;
}

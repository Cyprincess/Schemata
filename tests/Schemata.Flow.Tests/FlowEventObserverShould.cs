using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowEventObserverShould
{
    [Fact]
    public async SystemTask TwoCatchesSameName_DistinctSubscriptions() {
        var store   = new RecordingSubscriptionStore();
        var advisor = new FlowEventTransitionAdvisor(store);
        var advice  = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Id = "catch-a", Position = EventPosition.IntermediateCatch, Definition = new Message { Name = "invoice_paid" },
        });
        definition.Elements.Add(new FlowEvent {
            Id = "catch-b", Position = EventPosition.IntermediateCatch, Definition = new Message { Name = "invoice_paid" },
        });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(advice, new() {
            Process    = process,
            Definition = definition,
            Instance   = new() { WaitingAtId = "catch-a" },
        });

        await advisor.AdviseAsync(advice, new() {
            Process    = process,
            Definition = definition,
            Instance   = new() { WaitingAtId = "catch-b" },
        });

        Assert.Equal(2, store.Added.Count);
        Assert.Equal(2, store.Added.Select(s => s.Id).Distinct().Count());
        Assert.All(store.Added, s => Assert.Equal("invoice_paid", s.EventType));
    }

    private sealed class RecordingSubscriptionStore : IEventSubscriptionStore
    {
        public List<IEventSubscription> Added { get; } = [];

        public SystemTask AddAsync(IEventSubscription subscription, CancellationToken ct = default) {
            Added.Add(subscription);
            return SystemTask.CompletedTask;
        }

        public SystemTask RemoveAsync(string subscriptionId, CancellationToken ct = default) {
            return SystemTask.CompletedTask;
        }

        public System.Threading.Tasks.Task<IReadOnlyList<IEventSubscription>> FindAsync(
            string            eventType,
            string?           correlationKey = null,
            CancellationToken ct             = default
        ) {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<IEventSubscription>>([]);
        }
    }
}

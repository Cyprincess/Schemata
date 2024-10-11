using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Automatonymous;
using Automatonymous.Graphing;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Schemata.Workflow.Skeleton;

public static class StateMachineBaseExtensions
{
    public static Task RaiseEventAsync<TI, TEvent>(
        this StateMachineBase<TI> machine,
        TI                        instance,
        TEvent                    @event,
        CancellationToken         ct) where TI : class, IStatefulEntity
                                      where TEvent : class, IEvent {
        ct.ThrowIfCancellationRequested();
        return machine.RaiseEvent(instance, _ => machine.GetEvent<TEvent>(@event.Event), @event, ct);
    }

    public static async Task<IEnumerable<string>> GetNextEventsAsync<TI>(
        this StateMachineBase<TI> machine,
        TI                        instance,
        CancellationToken         ct) where TI : class, IStatefulEntity {
        ct.ThrowIfCancellationRequested();
        var events = await machine.NextEvents(instance);
        return events.Select(e => e.Name);
    }

    public static Task<StateMachineGraph> GetGraphAsync<TI>(this StateMachineBase<TI> machine, CancellationToken ct)
        where TI : class, IStatefulEntity {
        ct.ThrowIfCancellationRequested();
        var graph = machine.GetGraph();
        return Task.FromResult(graph);
    }
}

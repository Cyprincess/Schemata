using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class SignalBroadcastProcess : ProcessDefinition
{
    public SignalBroadcastProcess() {
        var start  = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var signal = new Signal { Name    = "broadcast-signal" };
        var catchEvent = new FlowEvent {
            Name       = "signal-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = signal,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, end]);
        Signals.Add(signal);
        Flows.Add(new() { Source = start, Target      = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}
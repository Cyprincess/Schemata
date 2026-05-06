using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

public class FlowEvent : FlowElement
{
    public EventPosition Position { get; set; }

    public IEventDefinition? Definition { get; set; }

    public bool Interrupting { get; set; } = true;

    public Activity? AttachedTo { get; set; }

    public bool IsTerminate { get; set; }

    public List<SequenceFlow> Incoming { get; } = [];

    public List<SequenceFlow> Outgoing { get; } = [];
}

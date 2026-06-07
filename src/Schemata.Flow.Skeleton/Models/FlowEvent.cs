using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>BPMN event (Start, Intermediate Catch/Throw, Boundary, End).</summary>
public class FlowEvent : FlowElement
{
    /// <summary>Where this event sits in the process graph.</summary>
    public EventPosition Position { get; set; }

    /// <summary>Optional event-definition payload (Message, Timer, Signal, Error, etc.).</summary>
    public IEventDefinition? Definition { get; set; }

    /// <summary>For boundary events: whether catching the event cancels the host activity.</summary>
    public bool Interrupting { get; set; } = true;

    /// <summary>For boundary events: the host activity this event attaches to.</summary>
    public Activity? AttachedTo { get; set; }

    /// <summary>For end events: whether reaching this event terminates the whole process scope.</summary>
    public bool IsTerminate { get; set; }

    /// <summary>Sequence flows entering this event.</summary>
    public List<SequenceFlow> Incoming { get; } = [];

    /// <summary>Sequence flows leaving this event.</summary>
    public List<SequenceFlow> Outgoing { get; } = [];
}

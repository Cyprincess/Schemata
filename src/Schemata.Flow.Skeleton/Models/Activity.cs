using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN activity (Task, SubProcess, CallActivity).</summary>
public abstract class Activity : FlowElement
{
    /// <summary>Optional loop characteristics (standard or multi-instance).</summary>
    public LoopCharacteristics? LoopCharacteristics { get; set; }

    /// <summary>Fallback outgoing sequence flow taken after other conditions fail.</summary>
    public SequenceFlow? DefaultFlow { get; set; }

    public List<SequenceFlow> Incoming { get; } = [];

    public List<SequenceFlow> Outgoing { get; } = [];
}

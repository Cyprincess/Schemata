using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN activity (Task, SubProcess, CallActivity).</summary>
public abstract class Activity : FlowElement
{
    /// <summary>Optional loop characteristics (standard or multi-instance).</summary>
    public LoopCharacteristics? LoopCharacteristics { get; set; }

    /// <summary>Whether this activity participates in compensation handling.</summary>
    public bool IsForCompensation { get; set; }

    /// <summary>Fallback outgoing sequence flow taken after other conditions fail.</summary>
    public SequenceFlow? DefaultFlow { get; set; }

    /// <summary>Sequence flows entering this activity.</summary>
    public List<SequenceFlow> Incoming { get; } = [];

    /// <summary>Sequence flows leaving this activity.</summary>
    public List<SequenceFlow> Outgoing { get; } = [];
}

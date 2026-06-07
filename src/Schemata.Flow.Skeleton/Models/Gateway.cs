using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN gateway (Exclusive, Parallel, Inclusive, EventBased, Complex).</summary>
public abstract class Gateway : FlowElement
{
    /// <summary>Sequence flows entering this gateway.</summary>
    public List<SequenceFlow> Incoming { get; } = [];

    /// <summary>Sequence flows leaving this gateway.</summary>
    public List<SequenceFlow> Outgoing { get; } = [];
}

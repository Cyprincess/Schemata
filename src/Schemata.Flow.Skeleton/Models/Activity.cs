using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

public abstract class Activity : FlowElement
{
    public LoopCharacteristics? LoopCharacteristics { get; set; }

    public bool IsForCompensation { get; set; }

    public SequenceFlow? DefaultFlow { get; set; }

    public List<SequenceFlow> Incoming { get; } = [];

    public List<SequenceFlow> Outgoing { get; } = [];
}

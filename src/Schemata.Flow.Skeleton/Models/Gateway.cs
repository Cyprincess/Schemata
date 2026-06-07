using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

public abstract class Gateway : FlowElement
{
    public List<SequenceFlow> Incoming { get; } = [];

    public List<SequenceFlow> Outgoing { get; } = [];
}

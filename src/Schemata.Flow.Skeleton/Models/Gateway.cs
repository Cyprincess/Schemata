namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN gateway (Exclusive, Parallel, Inclusive, EventBased, Complex).</summary>
public abstract class Gateway : FlowElement
{
    /// <summary>Sequence flows entering this gateway.</summary>
    public FlowGraphCollection<SequenceFlow> Incoming { get; } = new();

    /// <summary>Sequence flows leaving this gateway.</summary>
    public FlowGraphCollection<SequenceFlow> Outgoing { get; } = new();

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        Incoming.Freeze();
        Outgoing.Freeze();
    }
}

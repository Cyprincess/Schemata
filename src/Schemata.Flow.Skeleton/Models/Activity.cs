namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN activity (Task, SubProcess, CallActivity).</summary>
public abstract class Activity : FlowElement
{
    private LoopCharacteristics? _loopCharacteristics;
    private SequenceFlow? _defaultFlow;

    /// <summary>Optional loop characteristics (standard or multi-instance).</summary>
    public LoopCharacteristics? LoopCharacteristics {
        get => _loopCharacteristics;
        set {
            EnsureMutable();
            _loopCharacteristics = value;
        }
    }

    /// <summary>Fallback outgoing sequence flow taken after other conditions fail.</summary>
    public SequenceFlow? DefaultFlow {
        get => _defaultFlow;
        set {
            EnsureMutable();
            _defaultFlow = value;
        }
    }

    public FlowGraphCollection<SequenceFlow> Incoming { get; } = new();

    public FlowGraphCollection<SequenceFlow> Outgoing { get; } = new();

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        LoopCharacteristics?.Freeze();
        DefaultFlow?.Freeze();
        Incoming.Freeze();
        Outgoing.Freeze();
    }
}

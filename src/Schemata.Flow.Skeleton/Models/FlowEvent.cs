namespace Schemata.Flow.Skeleton.Models;

/// <summary>BPMN event (Start, Intermediate Catch/Throw, Boundary, End).</summary>
public class FlowEvent : FlowElement
{
    private EventPosition _position;
    private IEventDefinition? _definition;
    private bool _interrupting = true;
    private Activity? _attachedTo;
    private bool _isTerminate;

    /// <summary>Where this event sits in the process graph.</summary>
    public EventPosition Position {
        get => _position;
        set {
            EnsureMutable();
            _position = value;
        }
    }

    /// <summary>Optional event-definition payload (Message, Timer, Signal, Error, etc.).</summary>
    public IEventDefinition? Definition {
        get => _definition;
        set {
            EnsureMutable();
            _definition = value;
        }
    }

    /// <summary>For boundary events: whether catching the event cancels the host activity.</summary>
    public bool Interrupting {
        get => _interrupting;
        set {
            EnsureMutable();
            _interrupting = value;
        }
    }

    /// <summary>For boundary events: the host activity this event attaches to.</summary>
    public Activity? AttachedTo {
        get => _attachedTo;
        set {
            EnsureMutable();
            _attachedTo = value;
        }
    }

    /// <summary>For end events: whether reaching this event terminates the whole process scope.</summary>
    public bool IsTerminate {
        get => _isTerminate;
        set {
            EnsureMutable();
            _isTerminate = value;
        }
    }

    /// <summary>Sequence flows entering this event.</summary>
    public FlowGraphCollection<SequenceFlow> Incoming { get; } = new();

    /// <summary>Sequence flows leaving this event.</summary>
    public FlowGraphCollection<SequenceFlow> Outgoing { get; } = new();

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        AttachedTo?.Freeze();
        if (Definition is FlowGraphNode node) {
            node.Freeze();
        }

        Incoming.Freeze();
        Outgoing.Freeze();
    }
}

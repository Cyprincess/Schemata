namespace Schemata.Push.Skeleton;

/// <summary>Relative delivery priority a transport maps onto its backend's priority model.</summary>
public enum PushPriority
{
    /// <summary>Best-effort delivery; deferrable.</summary>
    Low,

    /// <summary>Default delivery priority.</summary>
    Normal,

    /// <summary>Expedited delivery.</summary>
    High,

    /// <summary>Time-critical delivery.</summary>
    Urgent,
}

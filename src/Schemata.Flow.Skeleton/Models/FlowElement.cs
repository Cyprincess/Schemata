namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN element placed inside a <see cref="ProcessDefinition" />.</summary>
public abstract class FlowElement
{
    /// <summary>Stable engine identifier referenced by sequence flows and the runtime.</summary>
    public string Id { get; set; } = null!;

    /// <summary>Human-readable label.</summary>
    public string Name { get; set; } = null!;
}

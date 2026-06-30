namespace Schemata.Flow.Skeleton.Models;

/// <summary>Base type of every BPMN element placed inside a <see cref="ProcessDefinition" />.</summary>
public abstract class FlowElement
{
    /// <summary>
    ///     Canonical element name: the element's identity within its process definition.
    ///     Unique across the definition and deterministic across definition rebuilds, so it is
    ///     the resume key persisted on process tokens. Audit rows and error payloads surface it
    ///     as the element label.
    /// </summary>
    public string Name { get; set; } = null!;
}

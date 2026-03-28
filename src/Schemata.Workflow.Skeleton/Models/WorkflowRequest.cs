using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

/// <summary>
///     Request payload for creating a new workflow, containing the entity type and instance data.
/// </summary>
/// <typeparam name="TI">The stateful entity type.</typeparam>
public class WorkflowRequest<TI>
    where TI : class, IStateful
{
    /// <summary>
    ///     The fully qualified CLR type name of the entity to create.
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>
    ///     The entity instance data to associate with the new workflow.
    /// </summary>
    public virtual TI? Instance { get; set; }
}

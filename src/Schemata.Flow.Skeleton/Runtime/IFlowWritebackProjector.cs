using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Projects a process instance's runtime position onto its source business entity during a
///     transition. The projection is engine-specific: the single-token state-machine engine writes
///     the current <see cref="IStateful.State" />, while a multi-token BPMN engine projects every
///     live token. Registered as a keyed service under the engine name
///     (<see cref="IFlowRuntime.EngineName" />); when no projector is registered for an engine, the
///     runtime falls back to writing <see cref="ProcessInstance.State" /> onto an
///     <see cref="IStateful" /> source — the state-machine projection.
/// </summary>
public interface IFlowWritebackProjector
{
    /// <summary>The engine this projector serves, matching <see cref="IFlowRuntime.EngineName" />.</summary>
    string EngineName { get; }

    /// <summary>
    ///     Applies <paramref name="instance" />'s runtime position to <paramref name="sourceEntity" />.
    ///     Implementations mutate the loaded source entity in place; persistence and concurrency are
    ///     handled by the caller within the transition's unit of work.
    /// </summary>
    /// <param name="sourceEntity">The loaded source business entity to mutate.</param>
    /// <param name="process">The persisted process row carrying the new position.</param>
    /// <param name="instance">The freshly computed process instance.</param>
    void Project(object sourceEntity, SchemataProcess process, ProcessInstance instance);
}

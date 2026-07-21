using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Engine output of a single runtime operation.
///     Carries the in-place mutated <see cref="SchemataProcess" />, the full post-operation token
///     collection, and the new transition rows the engine produced. Handlers persist all three under
///     a single unit of work.
/// </summary>
public sealed class ProcessSnapshot : ICanonicalName
{
    /// <summary>The process entity reference, mutated in place by the engine and persisted by the handler.</summary>
    public required SchemataProcess Process { get; init; }

    /// <summary>
    ///     Full live + historical token set after this operation. Length 1 under the state-machine
    ///     engine; length N under the BPMN engine. Handlers persist this collection row-by-row
    ///     (<c>AddAsync</c> / <c>UpdateAsync</c>) inside the unit of work.
    /// </summary>
    public required IReadOnlyList<SchemataProcessToken> Tokens { get; init; }

    /// <summary>
    ///     New transition rows produced by this operation. Typically 1-2 rows under the state-machine
    ///     engine (<see cref="TransitionKind.Move" /> plus optional <see cref="TransitionKind.Cancel" />
    ///     / <see cref="TransitionKind.Fail" />); N rows under the BPMN engine. Handlers add each row
    ///     within the unit of work.
    /// </summary>
    public required IReadOnlyList<SchemataProcessTransition> Transitions { get; init; }

    /// <summary>Compensation handlers registered by the engine after the operation completes.</summary>
    public IReadOnlyList<ProcessCompensationBinding> CompensationBindings { get; init; } = [];

    #region ICanonicalName Members

    /// <summary>The bare leaf name of the underlying process, delegated from <see cref="Process" />.</summary>
    public string? Name {
        get => Process.Name;
        set => Process.Name = value;
    }

    /// <summary>The full canonical name of the underlying process, delegated from <see cref="Process" />.</summary>
    public string? CanonicalName {
        get => Process.CanonicalName;
        set => Process.CanonicalName = value;
    }

    #endregion
}

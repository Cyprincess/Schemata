using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine identity shared by every closed runtime registration, so the registry can verify that
///     the configured engine name resolves without knowing the host's entity types.
/// </summary>
public interface IFlowRuntime
{
    /// <summary>The unique name of this engine, used for keyed registration and lookup.</summary>
    string EngineName { get; }

    /// <summary>The execution features supported by this runtime.</summary>
    FlowRuntimeCapabilities Capabilities { get; }
}

/// <summary>
///     Pure runtime engine contract. Engines are registered keyed by <see cref="IFlowRuntime.EngineName" />
///     and called by the resource-method handlers. Engines do not load or persist state — handlers pass
///     the current process instance and live token set as inputs; engines compute the
///     post-operation <see cref="ProcessSnapshot" /> and return it for handlers to persist under
///     their unit of work.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public interface IFlowRuntime<TProcess, TToken, TTransition> : IFlowRuntime
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
{

    /// <summary>
    ///     Creates the initial token by locating the <c>Start</c> event and following its outgoing
    ///     <see cref="SequenceFlow" />. The handler persists every entity in the returned snapshot.
    /// </summary>
    ValueTask<ProcessSnapshot> StartAsync(
        ProcessDefinition definition,
        TProcess          process,
        FlowExecutionContext context,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Applies an event trigger to the addressed token. The state-machine engine accepts a
    ///     <see langword="null" /> <paramref name="tokenName" /> because the process has exactly one
    ///     token; the BPMN engine requires <paramref name="tokenName" /> when more than one ready
    ///     token exists.
    /// </summary>
    ValueTask<ProcessSnapshot> TriggerAsync(
        ProcessDefinition    definition,
        TProcess             process,
        IReadOnlyList<TToken> tokens,
        FlowExecutionContext context,
        IEventDefinition     trigger,
        object?              payload,
        string?              tokenName = null,
        CancellationToken    ct        = default
    );

    /// <summary>
    ///     Auto-advances the addressed token by following unconditional or conditional outgoing
    ///     <see cref="SequenceFlow" />s. Returns the input snapshot semantics when the token is
    ///     suspended awaiting an external trigger.
    /// </summary>
    ValueTask<ProcessSnapshot> AdvanceAsync(
        ProcessDefinition     definition,
        TProcess              process,
        IReadOnlyList<TToken> tokens,
        FlowExecutionContext  context,
        string?               tokenName = null,
        CancellationToken     ct        = default
    );

    /// <summary>
    ///     Finds waiting tokens that can consume the supplied event trigger. The returned values are
    ///     token canonical names that callers may pass to <see cref="IFlowRuntime{TProcess,TToken,TTransition}.TriggerAsync" />.
    ///     The probe runs inside the same operation as the subsequent trigger, so
    ///     <paramref name="context" /> carries the same unit of work and scoped service provider.
    /// </summary>
    ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
        ProcessDefinition     definition,
        TProcess              process,
        IReadOnlyList<TToken> tokens,
        FlowExecutionContext  context,
        IEventDefinition      trigger,
        CancellationToken     ct = default
    );
}

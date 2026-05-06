using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     The context passed to an <see cref="IConditionExpression.Evaluate" />
///     invocation during flow traversal. Provides access to the process definition,
///     the running instance, variables, and the current state.
/// </summary>
public sealed class FlowConditionContext
{
    /// <summary>
    ///     The process definition that contains the flow being traversed.
    /// </summary>
    public ProcessDefinition Definition { get; set; } = null!;

    /// <summary>
    ///     The process instance being evaluated.
    /// </summary>
    public ProcessInstance Instance { get; set; } = null!;

    /// <summary>
    ///     A snapshot of the process variables at the time of evaluation.
    ///     Keys are <c>snake_case</c> names derived from CLR type names by convention.
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = [];

    /// <summary>
    ///     The <see cref="FlowElement.Name" /> of the element currently being
    ///     evaluated for outgoing transitions.
    /// </summary>
    public string CurrentState { get; set; } = null!;
}

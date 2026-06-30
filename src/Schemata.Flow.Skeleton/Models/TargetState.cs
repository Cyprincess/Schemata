namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Engine-internal description of the next element a token should advance onto. Returned
///     by the engines' <c>ResolveTargetStateAsync</c> walk and consumed by
///     <c>ApplyResolvedToToken</c> to mutate the token in place.
/// </summary>
/// <remarks>
///     Both <c>StateMachineEngine</c> and <c>BpmnEngine</c> use this same shape. Promoting it
///     to the skeleton keeps the engines aligned and lets shared helpers reason over a
///     common "next hop" concept.
/// </remarks>
/// <param name="StateName">
///     Name of the element the token will sit on. Element names are the canonical identity,
///     so the same value feeds token persistence, transition rows, and audit output.
/// </param>
/// <param name="WaitingAtName">Name of the catch event the token parks at, or <see langword="null" /> if active.</param>
/// <param name="IsComplete">
///     <see langword="true" /> when the resolved element consumes the token (end event,
///     compensation throw target reached).
/// </param>
public sealed record TargetState(
    string  StateName,
    string? WaitingAtName,
    bool    IsComplete);

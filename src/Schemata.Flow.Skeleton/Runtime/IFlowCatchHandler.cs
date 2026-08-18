using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Observers;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     The kinds of BPMN catch event that need a party outside the engine to deliver them.
/// </summary>
public enum FlowCatchKind
{
    /// <summary>A message catch, correlated to one token.</summary>
    Message,

    /// <summary>A signal catch, broadcast across the process.</summary>
    Signal,

    /// <summary>A timer catch, fired by the scheduler.</summary>
    Timer,
}

/// <summary>
///     Delivers a kind of BPMN catch event. A handler owns whatever arrangement outside the engine its
///     kind needs — an event-bus subscription, a scheduled timer job — and keeps that arrangement in
///     step with the token as it parks on and leaves catches.
/// </summary>
/// <remarks>
///     Flow requires some handler to answer for a kind before a token may park on it: a kind nothing
///     answers for is a kind nothing will ever deliver, so the run fails at the park rather than
///     hanging forever. The check asks whether the catch has an owner, never which package is
///     installed.
///     <para>
///         Pick this contract only to own a catch kind. Arming is infrastructure the engine
///         requires, not advice, so it runs unconditionally after the
///         <see cref="IFlowTransitionAdvisor" /> pipeline and no advisor can short-circuit it —
///         and, unlike an advisor, a handler carries no order and cannot veto. Per-transition work
///         that is not catch delivery belongs on <see cref="IFlowTransitionAdvisor" />; claiming a
///         kind this handler does not actually deliver would mask it from the fail-closed check and
///         park the token forever.
///     </para>
/// </remarks>
public interface IFlowCatchHandler
{
    /// <summary>
    ///     Whether this handler delivers catches of <paramref name="kind" />.
    /// </summary>
    /// <param name="kind">The catch kind a token is about to wait on.</param>
    /// <returns><see langword="true" /> when this handler services that kind.</returns>
    bool Handles(FlowCatchKind kind);

    /// <summary>
    ///     Brings this handler's arrangements in line with <paramref name="context" />: arms the catches
    ///     the token now waits on, and releases the ones it just left.
    /// </summary>
    /// <remarks>
    ///     Runs inside the transition's unit of work, so a handler that persists state enlists its
    ///     repositories against <see cref="FlowTransitionContext.UnitOfWork" />; a handler that only
    ///     provisions external infrastructure may ignore it. A throw aborts the transition before
    ///     persistence, rolling back everything that joined the unit of work.
    /// </remarks>
    /// <param name="context">The transition being applied.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask ArmAsync(FlowTransitionContext context, CancellationToken ct = default);
}

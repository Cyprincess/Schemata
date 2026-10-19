namespace Schemata.Abstractions.Resource;

/// <summary>
///     Mutation-completion evidence carried by a <see cref="ResourceDispatchExecution" />. The
///     idempotency wrap releases a reservation only on <see cref="NotWritten" /> or
///     <see cref="RolledBack" />; every other state may correspond to a persisted write.
/// </summary>
public enum ResourceDispatchOutcome
{
    /// <summary>
    ///     The dispatch never reached a mutation owner: an advisor short-circuit or a denial
    ///     answered the request before any write could stage. The reservation is releasable.
    /// </summary>
    None,

    /// <summary>
    ///     A mutation owner started the dispatch but reported no completion. The write may or may
    ///     not have persisted; the reservation stays pending until its retention elapses.
    /// </summary>
    Reached,

    /// <summary>
    ///     The mutation owner confirmed nothing was staged. The reservation is releasable and a
    ///     retry re-runs deterministically.
    /// </summary>
    NotWritten,

    /// <summary>
    ///     The provider transaction completed under the commit owner. The reservation must not be
    ///     released even when response shaping, cache settlement, or a later commit sink fails.
    /// </summary>
    Committed,

    /// <summary>
    ///     The unit of work completed a rollback without a commit — an explicit
    ///     <c>IUnitOfWork.RollbackAsync</c>, a failed provider commit, or disposal of an incomplete
    ///     unit of work. The reservation is releasable and a retry is safe.
    /// </summary>
    RolledBack,
}

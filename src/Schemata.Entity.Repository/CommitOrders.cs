namespace Schemata.Entity.Repository;

/// <summary>
///     Ordering segments for unit-of-work commit sinks. Sinks run in ascending order; equal orders
///     keep registration sequence.
/// </summary>
public static class CommitOrders
{
    /// <summary>
    ///     Repository type-level notifications (<see cref="Advisors.IRepositoryCommittedAdvisor{TEntity}" />).
    ///     Runs before every domain segment.
    /// </summary>
    public const int Repository = 0;

    /// <summary>
    ///     Resource mutation post-commit behavior (pending events, report schedule sync, and other
    ///     <see cref="Advisors.IResourceMutationCommittedAdvisor{TEntity}" /> callbacks).
    /// </summary>
    public const int Resource = 100_000_000;

    /// <summary>
    ///     Domain lifecycle sinks enlisted directly by domain owners (e.g. tenant cache invalidation).
    /// </summary>
    public const int Domain = 200_000_000;
}

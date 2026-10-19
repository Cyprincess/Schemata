using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Prepares a post-commit callback for a resource mutation. Invoked by
///     <see cref="ResourceMutation{TEntity}" /> only, after the mutation staged successfully and before
///     the unit of work commits.
/// </summary>
/// <remarks>
///     <see cref="Prepare" /> must be side-effect free: it captures only this operation's data and
///     returns the callback to execute after commit, or <see langword="null" /> when nothing must run.
///     A throwing <see cref="Prepare" /> fails the owning operation; the outer owner rolls back. The
///     returned callback runs after the transaction commits; when several callbacks capture the same
///     entity, only the first one observes the pre-commit state it captured.
/// </remarks>
/// <typeparam name="TEntity">The mutated resource entity type.</typeparam>
public interface IResourceMutationCommittedAdvisor<TEntity> : IAdvisor
    where TEntity : class
{
    /// <summary>
    ///     Captures this operation's post-commit work.
    /// </summary>
    /// <param name="entity">The mutated resource entity reference.</param>
    /// <param name="operation">The resource action intent of the mutation.</param>
    /// <returns>The post-commit callback, or <see langword="null" /> when no work is needed.</returns>
    Func<CancellationToken, Task>? Prepare(TEntity entity, Operations operation);
}

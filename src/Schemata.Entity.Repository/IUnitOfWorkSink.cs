using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

/// <summary>
///     Registration surface a <see cref="IUnitOfWork" /> exposes so that an enlisting
///     repository can attach commit and rollback callbacks without knowing the concrete
///     unit-of-work type. Implemented by provider unit-of-work types and consumed by
///     <see cref="RepositoryBase{TEntity}" /> when a repository enlists.
/// </summary>
public interface IUnitOfWorkSink
{
    /// <summary>
    ///     Adds a callback invoked after the unit of work commits its transaction, used to
    ///     dispatch the enlisting repository's
    ///     <see cref="IRepositoryCommittedAdvisor{TEntity}" /> pipeline.
    /// </summary>
    /// <param name="sink">The post-commit callback.</param>
    void AddCommitSink(Func<CancellationToken, Task> sink);

    /// <summary>
    ///     Adds a callback invoked when the unit of work rolls back or is disposed without a
    ///     commit, used to reset the enlisting repository's tracking state.
    /// </summary>
    /// <param name="reset">The rollback callback.</param>
    void AddRollbackSink(Action reset);
}

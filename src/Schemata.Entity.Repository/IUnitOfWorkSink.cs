using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Registration surface a <see cref="IUnitOfWork" /> exposes so that an enlisting
///     repository can attach commit and rollback callbacks through a common contract.
///     Implemented by provider unit-of-work types and consumed by
///     <see cref="RepositoryBase{TEntity}" /> when a repository enlists.
/// </summary>
public interface IUnitOfWorkSink
{
    /// <summary>
    ///     Adds a callback invoked after the unit of work commits its transaction, which
    ///     dispatches the enlisting repository's
    ///     <see cref="IRepositoryCommittedAdvisor{TEntity}" /> pipeline.
    /// </summary>
    /// <param name="sink">The post-commit callback.</param>
    void AddCommitSink(Func<CancellationToken, Task> sink);

    /// <summary>
    ///     Adds a callback invoked when the unit of work rolls back or disposal abandons
    ///     pending work, which resets the enlisting repository's tracking state.
    /// </summary>
    /// <param name="reset">The rollback callback.</param>
    void AddRollbackSink(Action reset);
}

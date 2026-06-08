using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Advisor invoked after a repository commit completes.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
public interface IRepositoryCommittedAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, CommitChanges<TEntity>>
    where TEntity : class;

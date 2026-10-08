using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Type-level notification that a repository committed actual writes. Registered on the unit of
///     work at <see cref="CommitOrders.Repository" /> only after the repository staged at least one
///     write; a commit with no writes sends no notification. Entity-level post-commit behavior belongs
///     to <see cref="IResourceMutationCommittedAdvisor{TEntity}" /> on the mutation path.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
public interface IRepositoryCommittedAdvisor<TEntity> : IAdvisor<IRepository<TEntity>>
    where TEntity : class;

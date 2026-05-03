using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the update pipeline, before the entity changes are persisted.
///     Implementations run in <see cref="IAdvisor" />.<c>Order</c> sequence and receive the
///     <see cref="IRepository{TEntity}" /> instance plus the entity being updated.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
public interface IRepositoryUpdateAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;

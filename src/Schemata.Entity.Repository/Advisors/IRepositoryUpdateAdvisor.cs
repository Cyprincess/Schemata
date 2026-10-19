using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the update pipeline before the entity is persisted.
///     Implementations run in <see cref="IAdvisor" />.<c>Order</c> sequence and receive the
///     <see cref="IRepository{TEntity}" /> instance, the entity being updated, and this call's
///     <see cref="MutationOptions" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
public interface IRepositoryUpdateAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity, MutationOptions>
    where TEntity : class;

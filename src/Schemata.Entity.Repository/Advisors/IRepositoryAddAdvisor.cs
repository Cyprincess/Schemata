using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the add pipeline, before the entity is persisted.
///     Implementations run in <see cref="IAdvisor" />.<c>Order</c> sequence and receive the
///     <see cref="IRepository{TEntity}" /> instance, the entity being added, and this call's
///     <see cref="MutationOptions" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
public interface IRepositoryAddAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity, MutationOptions>
    where TEntity : class;

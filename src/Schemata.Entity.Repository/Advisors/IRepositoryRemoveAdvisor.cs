using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the remove pipeline before persistence.
///     Implementations run in <see cref="IAdvisor" />.<c>Order</c> sequence and receive the
///     <see cref="IRepository{TEntity}" /> instance, the entity being removed, and this call's
///     <see cref="MutationOptions" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being removed.</typeparam>
public interface IRepositoryRemoveAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity, MutationOptions>
    where TEntity : class;

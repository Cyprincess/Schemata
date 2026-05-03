using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the remove pipeline, before the entity is deleted.
///     Implementations run in <see cref="IAdvisor" />.<c>Order</c> sequence and receive the
///     <see cref="IRepository{TEntity}" /> instance plus the entity being removed.
///     Returning <see cref="AdviseResult.Handle" /> prevents the physical delete;
///     returning <see cref="AdviseResult.Block" /> aborts without deleting.
/// </summary>
/// <typeparam name="TEntity">The entity type being removed.</typeparam>
public interface IRepositoryRemoveAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;

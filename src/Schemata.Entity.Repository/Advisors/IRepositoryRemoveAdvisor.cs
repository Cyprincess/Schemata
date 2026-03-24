using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the remove (delete) pipeline, before the entity is removed.
/// </summary>
/// <typeparam name="TEntity">The entity type being removed.</typeparam>
/// <remarks>
///     Returning <see cref="AdviseResult.Continue" /> allows the pipeline to proceed.
///     Returning <see cref="AdviseResult.Block" /> or <see cref="AdviseResult.Handle" /> aborts the remove
///     and the entity is not physically deleted.
///     The soft-delete advisor uses <see cref="AdviseResult.Handle" /> to convert deletes into updates.
/// </remarks>
public interface IRepositoryRemoveAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;

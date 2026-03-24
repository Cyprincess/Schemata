using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the add (insert) pipeline, before the entity is persisted.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     Returning <see cref="AdviseResult.Continue" /> allows the pipeline to proceed.
///     Returning <see cref="AdviseResult.Block" /> or <see cref="AdviseResult.Handle" /> aborts the add operation.
/// </remarks>
public interface IRepositoryAddAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;

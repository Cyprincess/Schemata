using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during the update pipeline, before the entity changes are persisted.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     Returning <see cref="AdviseResult.Continue" /> allows the pipeline to proceed.
///     Returning <see cref="AdviseResult.Block" /> or <see cref="AdviseResult.Handle" /> aborts the update operation.
/// </remarks>
public interface IRepositoryUpdateAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;

using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during query construction, before the user predicate is applied.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <remarks>
///     Use this to modify the <see cref="QueryContainer{TEntity}" /> (e.g., add global filters such as soft-delete).
///     The advisor receives the raw <see cref="QueryContainer{TEntity}" /> and may call
///     <see cref="QueryContainer{TEntity}.ApplyModification" /> to append query operators.
///     <see cref="AdviseResult" /> values are observed but all three currently allow the query to proceed.
/// </remarks>
public interface IRepositoryBuildQueryAdvisor<TEntity> : IAdvisor<QueryContainer<TEntity>>
    where TEntity : class;

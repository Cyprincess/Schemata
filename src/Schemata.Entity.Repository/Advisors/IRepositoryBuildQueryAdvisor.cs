using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked during query construction, before the user predicate is applied.
///     Receives a <see cref="QueryContainer{TEntity}" /> and may call
///     <see cref="QueryContainer{TEntity}.ApplyModification" /> to append global filters
///     (e.g., soft-delete exclusion via <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>/
///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>).
///     All three <see cref="AdviseResult" /> values allow the query to proceed.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
public interface IRepositoryBuildQueryAdvisor<TEntity> : IAdvisor<QueryContainer<TEntity>>
    where TEntity : class;

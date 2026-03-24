namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses the soft-delete query filter.
/// </summary>
/// <remarks>
///     Set via <see cref="IRepository.SuppressQuerySoftDelete" /> or <see cref="IRepository{TEntity}.SuppressQuerySoftDelete" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceBuildQuerySoftDelete{TEntity}" /> will not filter out soft-deleted entities,
///     allowing queries to return entities where <see cref="Schemata.Abstractions.Entities.ISoftDelete.DeleteTime" /> is non-null.
/// </remarks>
internal sealed class SuppressQuerySoftDelete;

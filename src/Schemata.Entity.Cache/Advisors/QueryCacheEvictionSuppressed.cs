namespace Schemata.Entity.Cache.Advisors;

/// <summary>
///     Context flag that suppresses cache eviction during update and remove operations.
/// </summary>
/// <remarks>
///     Set via
///     <see cref="Schemata.Entity.Repository.RepositoryExtensions.SuppressQueryCacheEviction(Schemata.Entity.Repository.IRepository)" />
///     or its generic overload.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceUpdateEvictCache{TEntity}" /> and
///     <see cref="AdviceRemoveEvictCache{TEntity}" /> skip eviction.
/// </remarks>
public sealed class QueryCacheEvictionSuppressed;

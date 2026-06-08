namespace Schemata.Entity.Cache.Advisors;

/// <summary>
///     Context flag that suppresses cache eviction during update and remove operations.
/// </summary>
/// <remarks>
///     Set via
///     <see cref="Schemata.Entity.Repository.RepositoryExtensions.SuppressQueryCacheEviction" />
///     or its generic overload.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceCommittedEvictCache{TEntity}" /> skips eviction.
/// </remarks>
public sealed class QueryCacheEvictionSuppressed;

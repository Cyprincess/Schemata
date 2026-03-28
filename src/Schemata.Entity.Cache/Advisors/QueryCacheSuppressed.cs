namespace Schemata.Entity.Cache.Advisors;

/// <summary>
///     Context flag that suppresses query-level caching for both read and write cache advisors.
/// </summary>
/// <remarks>
///     Set via the
///     <see
///         cref="Schemata.Entity.Repository.RepositoryExtensions.SuppressQueryCache(Schemata.Entity.Repository.IRepository)" />
///     extension method on <see cref="Schemata.Entity.Repository.IRepository" /> or
///     <see cref="Schemata.Entity.Repository.IRepository{TEntity}" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceQueryCache{TEntity,TResult,T}" /> will not return cached results and
///     <see cref="AdviceResultCache{TEntity,TResult,T}" /> will not store results in the cache.
/// </remarks>
internal sealed class QueryCacheSuppressed;

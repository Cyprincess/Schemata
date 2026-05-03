namespace Schemata.Entity.Cache.Advisors;

/// <summary>
///     Context flag that suppresses query-level caching for both
///     <see cref="AdviceQueryCache{TEntity,TResult,T}" /> and
///     <see cref="AdviceResultCache{TEntity,TResult,T}" />.
/// </summary>
/// <remarks>
///     Set via
///     <see cref="Schemata.Entity.Repository.RepositoryExtensions.SuppressQueryCache(Schemata.Entity.Repository.IRepository)" />
///     on <see cref="Schemata.Entity.Repository.IRepository" /> or
///     <see cref="Schemata.Entity.Repository.IRepository{TEntity}" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     the query advisor will not return cached results and the result advisor will not store them.
/// </remarks>
public sealed class QueryCacheSuppressed;

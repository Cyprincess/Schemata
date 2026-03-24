using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to enable in-memory query caching.
/// </summary>
public static class SchemataRepositoryBuilderExtensions
{
    /// <summary>
    ///     Registers in-memory query caching advisors (<see cref="AdviceQueryCache{TEntity,TResult,T}" /> and <see cref="AdviceResultCache{TEntity,TResult,T}" />).
    /// </summary>
    /// <param name="builder">The repository builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseQueryCache(this SchemataRepositoryBuilder builder) {
        builder.Services.AddMemoryCache();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryQueryAdvisor<,,>), typeof(AdviceQueryCache<,,>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryResultAdvisor<,,>), typeof(AdviceResultCache<,,>)));

        return builder;
    }
}

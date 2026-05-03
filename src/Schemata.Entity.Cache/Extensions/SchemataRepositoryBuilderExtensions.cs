using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Cache;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to enable query caching
///     together with immediate eviction on update and remove.
/// </summary>
public static class SchemataRepositoryBuilderExtensions
{
    /// <summary>
    ///     Registers the query, result, update-evict, and remove-evict cache advisors together with
    ///     <see cref="SchemataQueryCacheOptions" />.
    /// </summary>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">Optional callback to customize <see cref="SchemataQueryCacheOptions" />.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseQueryCache(
        this SchemataRepositoryBuilder     builder,
        Action<SchemataQueryCacheOptions>? configure = null
    ) {
        var options = builder.Services.AddOptions<SchemataQueryCacheOptions>();
        if (configure is not null) {
            options.Configure(configure);
        }

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryQueryAdvisor<,,>), typeof(AdviceQueryCache<,,>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryResultAdvisor<,,>), typeof(AdviceResultCache<,,>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateEvictCache<>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAdvisor<>), typeof(AdviceRemoveEvictCache<>)));

        return builder;
    }
}

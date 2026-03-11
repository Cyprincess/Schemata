using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataRepositoryBuilderExtensions
{
    public static SchemataRepositoryBuilder UseQueryCache(this SchemataRepositoryBuilder builder) {
        builder.Services.AddMemoryCache();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryQueryAdvisor<,,>), typeof(AdviceQueryCache<,,>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryResultAdvisor<,,>), typeof(AdviceResultCache<,,>)));

        return builder;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Cache.Advices;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataRepositoryBuilderExtensions
{
    public static SchemataRepositoryBuilder UseQueryCache(this SchemataRepositoryBuilder builder) {
        builder.Services.AddMemoryCache();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryQueryAsyncAdvice<,,>), typeof(AdviceQueryCache<,,>)));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryResultAdvice<,,>), typeof(AdviceResultCache<,,>)));

        return builder;
    }
}

using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advices;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddResourceAdvices(this IServiceCollection services) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceRequestAdvice<>), typeof(AdviceRequestAuthorize<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvice<,>), typeof(AdviceCreateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceEditRequestAdvice<,>), typeof(AdviceEditRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceEditAdvice<,>), typeof(AdviceEditFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvice<>), typeof(AdviceDeleteFreshness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvice<,>), typeof(AdviceResponseFreshness<,>)));

        return services;
    }

    public static SchemataResourceBuilder UseResource(this IServiceCollection services) {
        return new(services);
    }
}

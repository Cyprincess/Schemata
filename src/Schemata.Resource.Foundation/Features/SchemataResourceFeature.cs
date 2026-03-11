using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Security;

namespace Schemata.Resource.Foundation.Features;

[DependsOn<SchemataRoutingFeature>]
[DependsOn("Schemata.Mapping.Foundation.Features.SchemataMappingFeature`1")]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase
{
    public override int Priority => 360_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped(typeof(ResourceOperationHandler<,,,>));

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvisor<>), typeof(AdviceDeleteFreshness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseIdempotency<,>)));

        services.TryAddSingleton<IIdempotencyStore, IdempotencyStore>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.IsDynamic) continue;

            Type[] types;
            try {
                types = assembly.GetExportedTypes();
            } catch {
                continue;
            }

            foreach (var type in types) {
                if (type.GetCustomAttribute<ResourceAttribute>() is not { } attribute) {
                    continue;
                }

                RegisterResource(services, attribute);
            }
        }
    }

    public static void RegisterResource(IServiceCollection services, ResourceAttribute resource) {
        resource.Endpoints ??= resource.Entity.GetCustomAttributes<ResourceEndpointAttributeBase>()
                                       .Select(a => a.Endpoint)
                                       .ToArray();

        var entity  = resource.Entity;
        var request = resource.Request!;
        var detail  = resource.Detail!;

        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IResourceCreateRequestAdvisor<,>).MakeGenericType(entity, request),
                                      typeof(AdviceCreateRequestIdempotency<,,>).MakeGenericType(entity, request, detail)));

        RegisterAccessProvider(services, entity, typeof(ListRequest));
        RegisterAccessProvider(services, entity, typeof(GetRequest));
        RegisterAccessProvider(services, entity, request);
        RegisterAccessProvider(services, entity, typeof(DeleteRequest));

        services.Configure<SchemataResourceOptions>(options => {
            options.Resources.TryAdd(resource.Entity.TypeHandle, resource);
        });
    }

    private static void RegisterAccessProvider(IServiceCollection services, Type entity, Type requestType) {
        services.AddAccessProvider(
            entity,
            typeof(ResourceRequestContext<>).MakeGenericType(requestType),
            typeof(ResourceAccessProvider<,>).MakeGenericType(entity, requestType));
    }
}

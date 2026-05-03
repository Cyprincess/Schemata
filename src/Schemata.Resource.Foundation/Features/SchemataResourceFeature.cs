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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Features;

/// <summary>
///     Core feature that registers the resource advisor pipeline, the
///     <see cref="IIdempotencyStore" /> implementation, and auto-discovers resources
///     decorated with <see cref="ResourceAttribute" />.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
[DependsOn("Schemata.Mapping.Foundation.Features.SchemataMappingFeature`1")]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase
{
    /// <summary>
    ///     The default priority for this feature.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 50_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
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

    /// <summary>
///     Registers a single resource: resolves endpoints, adds the idempotency advisor
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>, and stores the
    ///     <see cref="ResourceAttribute" /> in <see cref="SchemataResourceOptions" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="resource">The <see cref="ResourceAttribute" /> describing the resource.</param>
    public static void RegisterResource(IServiceCollection services, ResourceAttribute resource) {
        resource.Endpoints ??= resource.Entity.GetCustomAttributes<ResourceEndpointAttributeBase>()
                                       .Select(a => a.Endpoint)
                                       .ToArray();

        var entity  = resource.Entity;
        var request = resource.Request!;
        var detail  = resource.Detail!;

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>).MakeGenericType(entity, request), typeof(AdviceCreateRequestIdempotency<,,>).MakeGenericType(entity, request, detail)));

        services.Configure<SchemataResourceOptions>(options => {
            options.Resources.TryAdd(resource.Entity.TypeHandle, resource);
        });
    }
}

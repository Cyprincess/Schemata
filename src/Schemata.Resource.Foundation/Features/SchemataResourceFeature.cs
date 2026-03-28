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
///     Core feature that registers the resource advisor pipeline, idempotency store, and auto-discovered resources.
/// </summary>
/// <remarks>
///     <para>Auto-registers the following advisors for all resources:</para>
///     <list type="bullet">
///         <item>
///             <see cref="AdviceCreateRequestValidation{TEntity,TRequest}" /> (order
///             <see cref="AdviceCreateRequestValidation.DefaultOrder" />)
///         </item>
///         <item>
///             <see cref="AdviceUpdateRequestValidation{TEntity, TRequest}" /> (order
///             <see cref="AdviceUpdateRequestValidation.DefaultOrder" />)
///         </item>
///         <item>
///             <see cref="AdviceUpdateFreshness{TEntity, TRequest}" /> (order
///             <see cref="AdviceUpdateFreshness.DefaultOrder" />)
///         </item>
///         <item>
///             <see cref="AdviceDeleteFreshness{TEntity}" /> (order <see cref="AdviceDeleteFreshness.DefaultOrder" />)
///         </item>
///         <item>
///             <see cref="AdviceResponseFreshness{TEntity, TDetail}" /> (order
///             <see cref="AdviceResponseFreshness.DefaultOrder" />)
///         </item>
///         <item>
///             <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> (order
///             <see cref="AdviceResponseIdempotency.DefaultOrder" />)
///         </item>
///     </list>
///     <para>
///         Per-resource registration via <see cref="RegisterResource" /> also adds
///         <see cref="AdviceCreateRequestIdempotency{TEntity, TRequest, TDetail}" /> (order 50,000,000).
///     </para>
/// </remarks>
[DependsOn<SchemataRoutingFeature>]
[DependsOn("Schemata.Mapping.Foundation.Features.SchemataMappingFeature`1")]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase
{
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
    ///     Registers a single resource type, configuring its idempotency advisor and options.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="resource">The resource attribute describing entity/request/detail/summary types.</param>
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

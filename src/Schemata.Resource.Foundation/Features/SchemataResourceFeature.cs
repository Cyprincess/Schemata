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
using Schemata.Expressions.Aip;
using Schemata.Resource.Foundation.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Features;

/// <summary>
///     Core feature that registers the resource advisor pipeline, and
///     auto-discovers resources decorated with <see cref="ResourceAttribute" />.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
[DependsOn("Schemata.Mapping.Foundation.Features.SchemataMappingFeature`1")]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase
{
    public const int DefaultPriority = Orders.Extension + 90_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped(typeof(ResourceOperationHandler<,,,>));
        services.TryAddScoped(typeof(ResourceMethodOperationHandler<,,>));
        services.AddAipExpressions();

        services.AddHttpContextAccessor();

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestSanitize<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestSanitize<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvisor<>), typeof(AdviceDeleteFreshness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseIdempotency<,>)));

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
    ///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>, scans
    ///     AIP-136 custom methods declared via <see cref="ResourceMethodAttribute" />, and stores the
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
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>).MakeGenericType(entity, request), typeof(AdviceUpdateRequestIdempotency<,,>).MakeGenericType(entity, request, detail)));

        var methods = entity.GetCustomAttributes<ResourceMethodAttribute>().ToList();
        foreach (var method in methods) {
            var handlerInterface = FindResourceMethodHandlerInterface(method.Handler);
            if (handlerInterface is null) {
                throw new InvalidOperationException(
                    $"Handler '{method.Handler.FullName}' for verb '{method.Verb}' on resource "
                    + $"'{entity.FullName}' must implement IResourceMethodHandler<TEntity, TRequest, TResponse>.");
            }

            services.TryAddScoped(method.Handler);

            var arguments     = handlerInterface.GetGenericArguments();
            var methodRequest = arguments[1];
            var methodResponse = arguments[2];

            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>).MakeGenericType(entity, methodRequest), typeof(AdviceMethodRequestAnonymous<,>).MakeGenericType(entity, methodRequest)));
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>).MakeGenericType(entity, methodRequest), typeof(AdviceMethodRequestAuthorize<,>).MakeGenericType(entity, methodRequest)));
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>).MakeGenericType(entity, methodRequest), typeof(AdviceMethodRequestIdempotency<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse), typeof(AdviceMethodFreshness<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
        }

        services.Configure<SchemataResourceOptions>(options => {
            if (!options.Resources.TryGetValue(entity.TypeHandle, out var r)) {
                options.Resources[entity.TypeHandle] = resource;
            } else if (r.Endpoints is null || resource.Endpoints is null) {
                r.Endpoints = null;
            } else {
                foreach (var ep in resource.Endpoints) {
                    if (!r.Endpoints.Contains(ep)) {
                        r.Endpoints.Add(ep);
                    }
                }
            }

            if (methods.Count == 0) {
                return;
            }

            if (!options.Methods.TryGetValue(entity.TypeHandle, out var existing)) {
                options.Methods[entity.TypeHandle] = [..methods];
                return;
            }

            var byVerb = existing.ToDictionary(m => m.Verb, m => m);
            foreach (var m in methods) {
                byVerb[m.Verb] = m;
            }
            existing.Clear();
            existing.AddRange(byVerb.Values);
        });
    }

    private static Type? FindResourceMethodHandlerInterface(Type handler) {
        foreach (var iface in handler.GetInterfaces()) {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>)) {
                return iface;
            }
        }
        return null;
    }
}

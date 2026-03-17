using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Reflection;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;
using Schemata.Resource.Grpc.Interceptors;

namespace Schemata.Resource.Grpc.Features;

[DependsOn<SchemataResourceFeature>]
public sealed class SchemataGrpcResourceFeature : FeatureBase
{
    private static readonly MethodInfo? MapGrpcServiceMethod = typeof(GrpcEndpointRouteBuilderExtensions)
                                                              .GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                              .FirstOrDefault(m => m is {
                                                                   Name: nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService),
                                                                   IsGenericMethodDefinition: true,
                                                               } && m.GetParameters().Length == 1);

    public override int Priority => 360_200_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddHttpContextAccessor();

        services.AddCodeFirstGrpc(options => { options.Interceptors.Add<ExceptionMappingInterceptor>(); });

        services.TryAddSingleton<ExceptionMappingInterceptor>();

        // Register open generic so DI can construct ResourceService<A,B,C,D> for any resource
        services.TryAddScoped(typeof(ResourceService<,,,>));

        // Register RuntimeTypeModel and BinderConfiguration lazily
        services.TryAddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();
            var model   = RuntimeTypeModelConfigurator.Configure(options.Value);
            return model;
        });

        services.TryAddSingleton(sp => {
            var model      = sp.GetRequiredService<RuntimeTypeModel>();
            var marshaller = ProtoBufMarshallerFactory.Create(model);
            return BinderConfiguration.Create([marshaller], new ResourceServiceBinder());
        });

        // Register ReflectionService lazily from resource options
        services.TryAddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();
            var binder  = sp.GetRequiredService<BinderConfiguration>();

            var types = options.Value.Resources
                               .Where(r => r.Value.Endpoints is null
                                        || r.Value.Endpoints.Count == 0
                                        || r.Value.Endpoints.Any(e => e == GrpcResourceAttribute.Name))
                               .Select(r => typeof(IResourceService<,,,>).MakeGenericType(r.Value.Entity, r.Value.Request!, r.Value.Detail!, r.Value.Summary!))
                               .ToArray();

            return ReflectionServiceFactory.Create(binder, types);
        });
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var sp      = app.ApplicationServices;
        var options = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();

        app.UseEndpoints(endpoints => {
            var hasGrpcResources = false;

            foreach (var (_, resource) in options.Value.Resources) {
                if (resource.Endpoints is not null
                 && resource.Endpoints.Count != 0
                 && resource.Endpoints.All(e => e != GrpcResourceAttribute.Name)) {
                    continue;
                }

                var service = typeof(ResourceService<,,,>).MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!);

                // Call endpoints.MapGrpcService<T>() via reflection
                var result    = MapGrpcService(endpoints, service);
                var attribute = resource.Entity.GetCustomAttribute<RateLimitPolicyAttribute>();
                if (attribute is not null && result is GrpcServiceEndpointConventionBuilder builder) {
                    builder.RequireRateLimiting(attribute.PolicyName);
                }

                hasGrpcResources = true;
            }

            if (hasGrpcResources) {
                endpoints.MapGrpcService<ReflectionService>();
            }
        });
    }

    private static object? MapGrpcService(IEndpointRouteBuilder endpoints, Type serviceType) {
        return MapGrpcServiceMethod?.MakeGenericMethod(serviceType).Invoke(null, [endpoints]);
    }
}

using System;
using System.Linq;
using System.Reflection;
using Grpc.AspNetCore.Server.Model;
using Grpc.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;
using Schemata.Resource.Grpc.Interceptors;

namespace Schemata.Resource.Grpc.Features;

/// <summary>
///     Feature that configures gRPC transport for resources, including protobuf-net serialization, service mapping, and
///     gRPC reflection.
/// </summary>
[DependsOn<SchemataResourceFeature>]
public sealed class SchemataGrpcResourceFeature : FeatureBase
{
    public const int DefaultPriority = SchemataResourceFeature.DefaultPriority + 20_000_000;

    private static readonly MethodInfo? MapGrpcServiceMethod = typeof(GrpcEndpointRouteBuilderExtensions)
                                                              .GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                              .FirstOrDefault(m => m is {
                                                                   Name: nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService),
                                                                   IsGenericMethodDefinition: true,
                                                               } && m.GetParameters().Length == 1);

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
        services.AddHttpContextAccessor();

        services.AddCodeFirstGrpc(options => { options.Interceptors.Add<ExceptionMappingInterceptor>(); });

        services.TryAddSingleton<ExceptionMappingInterceptor>();

        // Register open generic so DI can construct ResourceService<A,B,C,D> for any resource
        services.TryAddScoped(typeof(ResourceService<,,,>));

        // Build ResourceBinderConfiguration lazily (isolates RuntimeTypeModel and BinderConfiguration from user DI)
        services.TryAddSingleton(sp => {
            var options    = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();
            var model      = RuntimeTypeModelConfigurator.Configure(options.Value);
            var marshaller = ProtoBufMarshallerFactory.Create(model);
            var binder     = BinderConfiguration.Create([marshaller], new ResourceServiceBinder());
            return new ResourceBinderConfiguration(model, binder);
        });

        // Forward BinderConfiguration for client-side usage (e.g. test factories)
        services.TryAddSingleton(sp => sp.GetRequiredService<ResourceBinderConfiguration>().Binder);

        // Open generic method provider — only activates for ResourceService<,,,> types;
        // no-op for proto-first or other gRPC services.
        services.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(ResourceServiceMethodProvider<>)));

        // Pre-register ReflectionServiceImpl before AddGrpcReflection() so our factory wins via TryAdd
        services.TryAddSingleton(sp => {
            var descriptors = BuildCodeFirstDescriptors(sp);
            return new ReflectionServiceImpl(descriptors);
        });

        services.AddGrpcReflection();
    }

    /// <inheritdoc />
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

                hasGrpcResources = true;

                var service = typeof(ResourceService<,,,>).MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!);

                // Call endpoints.MapGrpcService<T>() via reflection
                var result = MapGrpcService(endpoints, service);

                if (result is not IEndpointConventionBuilder builder) {
                    continue;
                }

                var quota = resource.Entity.GetCustomAttribute<RateLimitPolicyAttribute>();
                if (quota is not null) {
                    builder.RequireRateLimiting(quota.PolicyName);
                }

                if (!string.IsNullOrWhiteSpace(options.Value.AuthenticationScheme)) {
                    var policy = new AuthorizationPolicyBuilder(options.Value.AuthenticationScheme)
                                .RequireAssertion(_ => true)
                                .Build();
                    builder.RequireAuthorization(policy);
                }
            }

            if (hasGrpcResources) {
                endpoints.MapGrpcReflectionService();
            }
        });
    }

    private static Google.Protobuf.Reflection.ServiceDescriptor[] BuildCodeFirstDescriptors(IServiceProvider sp) {
        var config  = sp.GetRequiredService<ResourceBinderConfiguration>();
        var options = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();

        var types = options.Value.Resources
                           .Where(r => r.Value.Endpoints is null
                                    || r.Value.Endpoints.Count == 0
                                    || r.Value.Endpoints.Any(e => e == GrpcResourceAttribute.Name))
                           .Select(r => typeof(IResourceService<,,,>).MakeGenericType(r.Value.Entity, r.Value.Request!, r.Value.Detail!, r.Value.Summary!))
                           .ToArray();

        if (types.Length == 0) {
            return [];
        }

        return FileDescriptorBridge.BuildServiceDescriptors(config.Model, types).ToArray();
    }

    private static object? MapGrpcService(IEndpointRouteBuilder endpoints, Type serviceType) {
        return MapGrpcServiceMethod?.MakeGenericMethod(serviceType).Invoke(null, [endpoints]);
    }
}

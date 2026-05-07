using System;
using System.Linq;
using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
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
///     Feature that registers gRPC transport for resources: code-first protobuf-net
///     serialization, per-resource service routing, exception→status mapping,
///     and gRPC server reflection for tooling.
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

        // Open generic registration so DI can resolve ResourceService<A,B,C,D> for any
        // resource type combination.
        services.TryAddScoped(typeof(ResourceService<,,,>));

        // Build ResourceBinderConfiguration lazily — isolates RuntimeTypeModel and
        // BinderConfiguration from the global DI container so user-registered
        // protobuf-net.Grpc services are not affected.
        services.TryAddSingleton(sp => {
            var options    = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();
            var model      = RuntimeTypeModelConfigurator.Configure(options.Value);
            var marshaller = ProtoBufMarshallerFactory.Create(model);
            var binder     = BinderConfiguration.Create([marshaller], new ResourceServiceBinder());
            return new ResourceBinderConfiguration(model, binder);
        });

        // Forward BinderConfiguration for client-side usage (e.g. test factories).
        services.TryAddSingleton(sp => sp.GetRequiredService<ResourceBinderConfiguration>().Binder);

        // Open generic IServiceMethodProvider<> — grpc-dotnet discovers all registered
        // providers via IEnumerable, and ResourceServiceMethodProvider<> is a no-op
        // for TService types that are not closed ResourceService<,,,>.
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(ResourceServiceMethodProvider<>)));

        // Register reflection implementations with merged descriptors.
        // Code-first resource services are resolved via FileDescriptorBridge.
        // Proto-first services (user-registered or framework-provided) are
        // discovered from EndpointDataSource — each proto-first service base
        // class exposes a static Descriptor property holding the ServiceDescriptor.
        services.TryAddSingleton(sp => new ReflectionServiceImpl(MergeDescriptors(sp)));
        services.TryAddSingleton(sp => new ReflectionV1ServiceImpl(MergeDescriptors(sp)));
    }

    /// <inheritdoc />
    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        var sp      = app.ApplicationServices;
        var options = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();

        var hasGrpcResources = false;

        foreach (var (_, resource) in options.Value.Resources) {
            if (resource.Endpoints is not null
             && resource.Endpoints.Count != 0
             && resource.Endpoints.All(e => e != GrpcResourceAttribute.Name)) {
                continue;
            }

            hasGrpcResources = true;

            var service = typeof(ResourceService<,,,>).MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!);

            var result = MapGrpcService(endpoints, service);

            if (result is not IEndpointConventionBuilder builder) {
                continue;
            }

            var quota = resource.Entity.GetCustomAttribute<RateLimitPolicyAttribute>();
            if (quota is not null) {
                builder.RequireRateLimiting(quota.PolicyName);
            }

            // If an authentication scheme is configured, require authentication
            // but do NOT demand a specific authorization policy — policy evaluation
            // is deferred to the advisor pipeline (AIP-211).
            if (!string.IsNullOrWhiteSpace(options.Value.AuthenticationScheme)) {
                var policy = new AuthorizationPolicyBuilder(options.Value.AuthenticationScheme)
                            .RequireAssertion(_ => true)
                            .Build();
                builder.RequireAuthorization(policy);
            }
        }

        if (hasGrpcResources) {
            endpoints.MapGrpcService<ReflectionServiceImpl>();
            endpoints.MapGrpcService<ReflectionV1ServiceImpl>();
        }
    }

    private static Google.Protobuf.Reflection.ServiceDescriptor[] MergeDescriptors(IServiceProvider sp) {
        var codeFirst = BuildCodeFirstDescriptors(sp);
        var epd       = sp.GetRequiredService<EndpointDataSource>();
        var protoFirst = ResolveProtoFirstDescriptors(epd);
        return codeFirst.Concat(protoFirst).ToArray();
    }

    private static Google.Protobuf.Reflection.ServiceDescriptor[] ResolveProtoFirstDescriptors(
        EndpointDataSource epd
    ) {
        return epd.Endpoints
                  .Select(e => e.Metadata.GetMetadata<GrpcMethodMetadata>())
                  .Where(m => m is not null)
                  .Select(m => m!.ServiceType)
                  .Distinct()
                  .Select(GetServiceDescriptor)
                  .Where(d => d is not null)
                  .Cast<Google.Protobuf.Reflection.ServiceDescriptor>()
                  .ToArray();
    }

    private static Google.Protobuf.Reflection.ServiceDescriptor? GetServiceDescriptor(Type serviceType) {
        if (serviceType.IsConstructedGenericType
            && serviceType.GetGenericTypeDefinition() == typeof(ResourceService<,,,>)) {
            return null;
        }

        for (var t = serviceType; t is not null && t != typeof(object); t = t.BaseType) {
            var attr = t.GetCustomAttribute<BindServiceMethodAttribute>();
            if (attr is not null) {
                return attr.BindType
                           .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)
                           ?.GetValue(null) as Google.Protobuf.Reflection.ServiceDescriptor;
            }
        }

        return null;
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

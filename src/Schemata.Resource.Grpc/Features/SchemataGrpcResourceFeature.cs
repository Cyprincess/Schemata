using System;
using System.Linq;
using System.Reflection;
using Grpc.AspNetCore.Server.Model;
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
using Schemata.Resource.Grpc.Internal;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Features;

namespace Schemata.Resource.Grpc.Features;

/// <summary>
///     Feature that registers gRPC transport for resources: code-first protobuf-net
///     serialization, per-resource service routing, and a code-first reflection
///     contributor. Shared gRPC plumbing (<c>AddCodeFirstGrpc</c>,
///     <see cref="Schemata.Transport.Grpc.Interceptors.ExceptionMappingInterceptor" />,
///     and gRPC server reflection) is supplied by
///     <see cref="SchemataTransportGrpcFeature" />.
/// </summary>
[DependsOn<SchemataResourceFeature>]
[DependsOn<SchemataTransportGrpcFeature>]
public sealed class SchemataGrpcResourceFeature : FeatureBase
{
    /// <summary>
    ///     Default endpoint priority for resource gRPC endpoints.
    /// </summary>
    public const int DefaultPriority = SchemataResourceFeature.DefaultPriority + 200_000;

    private static readonly MethodInfo? MapGrpcServiceMethod = typeof(GrpcEndpointRouteBuilderExtensions)
                                                              .GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                              .FirstOrDefault(m => m is {
                                                                   Name: nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService),
                                                                   IsGenericMethodDefinition: true,
                                                               } && m.GetParameters().Length == 1);

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped(typeof(ResourceService<,,,>));

        services.TryAddSingleton(sp => {
            var options    = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();
            var model      = RuntimeTypeModelConfigurator.Configure(options.Value);
            var marshaller = ProtoBufMarshallerFactory.Create(model);
            var binder     = BinderConfiguration.Create([marshaller], new ResourceServiceBinder());
            return new ResourceBinderConfiguration(model, binder);
        });

        services.TryAddSingleton(sp => sp.GetRequiredService<ResourceBinderConfiguration>().Binder);

        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(ResourceServiceMethodProvider<>)));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGrpcServiceDescriptorContributor, ResourceGrpcServiceDescriptorContributor>());
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        var sp      = app.ApplicationServices;
        var options = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();

        foreach (var (_, resource) in options.Value.Resources) {
            if (!GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            var service = typeof(ResourceService<,,,>).MakeGenericType(resource.Entity, resource.Request!, resource.Detail!, resource.Summary!);

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
    }

    private static object? MapGrpcService(IEndpointRouteBuilder endpoints, Type serviceType) {
        return MapGrpcServiceMethod?.MakeGenericMethod(serviceType).Invoke(null, [endpoints]);
    }
}

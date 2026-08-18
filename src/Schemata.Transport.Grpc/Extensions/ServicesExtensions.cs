using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Configuration;
using Schemata.Common;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Interceptors;
using ProtoServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Code-first gRPC registration with protobuf-net and the
///     <see cref="SimpleRpcExceptionsInterceptor" />.
/// </summary>
public static class ServicesExtensions
{
    private const string DescriptorProperty = "Descriptor";

    /// <summary>
    ///     Adds gRPC with code-first protobuf-net support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional gRPC server options configuration.</param>
    /// <returns>The gRPC server builder.</returns>
    public static IGrpcServerBuilder AddCodeFirstGrpc(
        this IServiceCollection     services,
        Action<GrpcServiceOptions>? configureOptions
    ) {
        var builder = configureOptions is null ? services.AddGrpc() : services.AddGrpc(configureOptions);
        services.TryAddSingleton(SimpleRpcExceptionsInterceptor.Instance);
        return builder;
    }

    /// <summary>
    ///     Adds the shared gRPC transport services: code-first protobuf-net serialization, the
    ///     <see cref="ExceptionMappingInterceptor" /> and both gRPC server reflection services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataGrpcTransport(this IServiceCollection services) {
        services.AddHttpContextAccessor();

        services.AddCodeFirstGrpc(options => { options.Interceptors.Add<ExceptionMappingInterceptor>(); });

        services.TryAddSingleton<ExceptionMappingInterceptor>();

        services.TryAddSingleton(sp => new ReflectionServiceImpl(MergeDescriptors(sp)));
        services.TryAddSingleton(sp => new ReflectionV1ServiceImpl(MergeDescriptors(sp)));

        return services;
    }

    private static ProtoServiceDescriptor[] MergeDescriptors(IServiceProvider sp) {
        var contributed = sp.GetServices<IGrpcServiceDescriptorContributor>()
                            .SelectMany(c => c.GetServiceDescriptors(sp));
        var epd        = sp.GetRequiredService<EndpointDataSource>();
        var protoFirst = ResolveProtoFirstDescriptors(epd);
        return contributed.Concat(protoFirst).ToArray();
    }

    private static IEnumerable<ProtoServiceDescriptor> ResolveProtoFirstDescriptors(EndpointDataSource epd) {
        return epd.Endpoints
                  .Select(e => e.Metadata.GetMetadata<GrpcMethodMetadata>())
                  .Where(m => m is not null)
                  .Select(m => m!.ServiceType)
                  .Distinct()
                  .Select(GetServiceDescriptor)
                  .Where(d => d is not null)
                  .Cast<ProtoServiceDescriptor>();
    }

    private static ProtoServiceDescriptor? GetServiceDescriptor(Type serviceType) {
        for (var t = serviceType; t is not null && t != typeof(object); t = t.BaseType) {
            var attr = t.GetCustomAttribute<BindServiceMethodAttribute>();
            if (attr is not null) {
                return AppDomainTypeCache.GetStaticProperty(attr.BindType, DescriptorProperty)
                                         ?.GetValue(null) as ProtoServiceDescriptor;
            }
        }

        return null;
    }
}

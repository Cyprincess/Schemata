using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Transport.Grpc.Interceptors;
using Schemata.Transport.Grpc.Proto;
using static Schemata.Abstractions.SchemataConstants;
using ProtoServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace Schemata.Transport.Grpc.Features;

/// <summary>
///     Shared gRPC transport stack: code-first protobuf-net serialization, the
///     <see cref="ExceptionMappingInterceptor" />, gRPC server reflection, AIP-standard
///     request types pre-registered with trait field renames, and
///     <see cref="RuntimeTypeModel.Default" /> configuration for every type contributed
///     by an <see cref="IProtoTypeContributor" />.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
public sealed class SchemataTransportGrpcFeature : FeatureBase
{
    /// <summary>Second slot of the Extension priority range, adjacent to HTTP transport.</summary>
    public const int DefaultPriority = Orders.Extension + 20_000_000;

    public override int Priority => DefaultPriority;

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

        services.TryAddSingleton(sp => new ReflectionServiceImpl(MergeDescriptors(sp)));
        services.TryAddSingleton(sp => new ReflectionV1ServiceImpl(MergeDescriptors(sp)));
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, typeof(ListRequest));
        SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, typeof(DeleteRequest));

        var sp           = app.ApplicationServices;
        var contributors = sp.GetServices<IProtoTypeContributor>().ToList();
        if (contributors.Count == 0) {
            return;
        }

        var summaries = contributors.SelectMany(c => c.GetSummaryTypes(sp)).Distinct().ToList();
        if (summaries.Count > 0) {
            SchemataProtoModelConfigurator.ConfigureSummaryTypes(RuntimeTypeModel.Default, summaries);
        }

        var messages = contributors.SelectMany(c => c.GetMessageTypes(sp)).Distinct().ToList();
        foreach (var type in messages) {
            SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, type);
        }
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<ReflectionServiceImpl>();
        endpoints.MapGrpcService<ReflectionV1ServiceImpl>();
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
                return attr.BindType
                           .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)
                           ?.GetValue(null) as ProtoServiceDescriptor;
            }
        }

        return null;
    }
}

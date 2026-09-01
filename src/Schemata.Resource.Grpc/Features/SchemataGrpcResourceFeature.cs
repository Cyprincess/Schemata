using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc.Configuration;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.Building;
using Schemata.Resource.Foundation.Features;
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
            var registry   = sp.GetRequiredService<ResourceRegistry>();
            var model      = RuntimeTypeModelConfigurator.Configure(registry);
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
    ) => endpoints.MapSchemataGrpcResources(
        app.ApplicationServices.GetRequiredService<ResourceRegistry>(),
        app.ApplicationServices.GetRequiredService<IOptions<SchemataResourceOptions>>().Value.AuthenticationScheme);
}

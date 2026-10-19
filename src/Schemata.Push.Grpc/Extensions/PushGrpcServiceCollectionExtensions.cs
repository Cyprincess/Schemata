using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Push.Grpc;
using Schemata.Transport.Grpc;
using Grpc.AspNetCore.Server.Model;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Push gRPC control plane.</summary>
public static class PushGrpcServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Push control-plane gRPC service and the shared exception-mapping
    ///     interceptor. Methods require authentication and additionally the conventional policies
    ///     <c>push.subscriptions.create</c>, <c>push.subscriptions.get</c>,
    ///     <c>push.subscriptions.delete</c>, and the operator-only <c>push.send</c>. Hosts
    ///     configure these policies through the standard authorization builder.
    /// </summary>
    public static IServiceCollection AddSchemataPushGrpc(this IServiceCollection services) {
        services.AddSchemataGrpcTransport();

        services.TryAddScoped<PushControlGrpcService>();
        services.TryAddSingleton<PushGrpcModel>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceMethodProvider<PushControlGrpcService>, PushControlMethodProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGrpcServiceDescriptorContributor, PushGrpcServiceDescriptorContributor>());

        return services;
    }
}

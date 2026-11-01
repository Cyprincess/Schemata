using Grpc.AspNetCore.Server.Model;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.Skeleton;
using Schemata.Transport.Grpc;

namespace Microsoft.Extensions.DependencyInjection;

public static class StreamServiceExtensions
{
    public static IServiceCollection AddSchemataGrpcStream<TRequest, TItem>(this IServiceCollection services, string serviceName, string methodName)
        where TRequest : class, IStreamRequest<TItem> where TItem : class {
        services.AddSchemataGrpcTransport();
        services.TryAddSingleton<StreamDescriptorRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGrpcServiceDescriptorContributor, StreamDescriptorContributor>());
        services.AddSingleton<IServiceMethodProvider<StreamService<TRequest, TItem>>>(
            provider => new StreamRegistration<TRequest, TItem>(provider.GetRequiredService<StreamDescriptorRegistry>(), serviceName, methodName));
        return services;
    }

    public static GrpcServiceEndpointConventionBuilder MapSchemataGrpcStream<TRequest, TItem>(this IEndpointRouteBuilder endpoints)
        where TRequest : class, IStreamRequest<TItem> where TItem : class => endpoints.MapGrpcService<StreamService<TRequest, TItem>>();
}

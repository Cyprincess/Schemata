using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Quota.Grpc;
using Schemata.Transport.Grpc;

namespace Microsoft.Extensions.DependencyInjection;

public static class QuotaGrpcServiceCollectionExtensions
{
    public static IServiceCollection AddSchemataQuotaGrpc(this IServiceCollection services) {
        services.AddSchemataGrpcTransport();
        services.TryAddScoped<QuotaGrpcService>();
        services.TryAddSingleton<QuotaGrpcModel>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceMethodProvider<QuotaGrpcService>, QuotaGrpcMethodProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGrpcServiceDescriptorContributor, QuotaGrpcServiceDescriptorContributor>());
        return services;
    }
}

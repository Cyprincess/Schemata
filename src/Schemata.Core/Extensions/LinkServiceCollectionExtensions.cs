using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core.Linking;

namespace Microsoft.Extensions.DependencyInjection;

public static class LinkServiceCollectionExtensions
{
    public static IServiceCollection AddSchemataLinkContributor<T>(this IServiceCollection services)
        where T : class, ILinkContributor {
        services.TryAddEnumerable(ServiceDescriptor.KeyedScoped<ILinkContributor, T>(LinkTransports.Http));
        services.TryAddEnumerable(ServiceDescriptor.KeyedScoped<ILinkContributor, T>(LinkTransports.Grpc));
        return services;
    }
}

using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static SchemataResourceBuilder UseResource(this IServiceCollection services) {
        return new(services);
    }
}

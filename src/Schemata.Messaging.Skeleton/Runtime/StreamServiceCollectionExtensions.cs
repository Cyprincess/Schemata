using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;

namespace Microsoft.Extensions.DependencyInjection;

public static class StreamServiceCollectionExtensions
{
    public static IServiceCollection AddSchemataStreams(this IServiceCollection services) {
        services.TryAddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>();
        services.TryAddScoped<IStreamDispatcher, InProcessStreamDispatcher>();
        return services;
    }
}

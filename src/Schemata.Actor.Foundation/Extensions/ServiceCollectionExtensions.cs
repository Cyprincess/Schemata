using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the in-process actor runtime.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="SchemataActorOptions" />, the actor registry (seeded from
    ///     registrations staged on <see cref="SchemataActorOptions.Registrations" />), the
    ///     in-process actor system, and the default turn-scope factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataActor(this IServiceCollection services) {
        services.AddOptions<SchemataActorOptions>();

        services.TryAddSingleton<IActorSystem, InProcessActorSystem>();
        services.AddHostedService<ActorHostedService>();

        services.TryAddSingleton<IActorRegistry>(sp => {
            var registry      = new ActorRegistry();
            var registrations = sp.GetRequiredService<IOptions<SchemataActorOptions>>().Value.Registrations;
            foreach (var registration in registrations) {
                registry.Register(registration.ActorType, registration.Props);
            }

            return registry;
        });

        services.TryAddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>();

        return services;
    }
}

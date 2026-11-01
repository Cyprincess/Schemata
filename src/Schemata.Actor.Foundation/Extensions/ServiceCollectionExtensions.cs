using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
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
    ///     Registers the actor runtime closed on the default persisted actor-state entity
    ///     <see cref="SchemataActor" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataActor(this IServiceCollection services) {
        return services.AddSchemataActor<SchemataActor>();
    }

    /// <summary>
    ///     Registers <see cref="SchemataActorOptions" />, the actor registry (seeded from
    ///     registrations staged on <see cref="SchemataActorOptions.Registrations" />), the
    ///     in-process actor system factory closed on <typeparamref name="TActor" />, and the default
    ///     turn-scope factory.
    /// </summary>
    /// <typeparam name="TActor">The persisted actor-state entity type registered by the host.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataActor<TActor>(this IServiceCollection services)
        where TActor : SchemataActor, new() {
        services.AddOptions<SchemataActorOptions>();

        services.TryAddSingleton<IActorSystemFactory, InProcessActorSystemFactory<TActor>>();
        services.TryAddSingleton<IActorSystem>(sp => sp.GetRequiredService<IActorSystemFactory>().Create(sp));
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

    /// <summary>
    ///     Registers <typeparamref name="TFactory" /> as the factory that creates the host
    ///     <see cref="IActorSystem" />.
    /// </summary>
    /// <typeparam name="TFactory">The factory that owns the hosted actor system.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataActorSystem<TFactory>(this IServiceCollection services)
        where TFactory : class, IActorSystemFactory {
        services.Replace(ServiceDescriptor.Singleton<IActorSystemFactory, TFactory>());

        return services;
    }
}

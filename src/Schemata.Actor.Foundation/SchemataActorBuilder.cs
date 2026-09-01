using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Actor.Foundation;

/// <summary>
///     Fluent builder for configuring the actor system. Actor-type registrations added through
///     <see cref="Register{TActor}" /> are staged into <see cref="SchemataActorOptions" /> and read
///     back once when the <see cref="IActorRegistry" /> singleton is built.
/// </summary>
public sealed class SchemataActorBuilder
{
    /// <summary>Initializes the builder bound to the given options and service collection.</summary>
    /// <param name="schemata">Receives the feature registrations added through <see cref="AddFeature{T}" />.</param>
    /// <param name="services">The service collection that receives actor registrations.</param>
    public SchemataActorBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    private SchemataOptions Schemata { get; }

    /// <summary>Service collection that receives actor registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }

    /// <summary>
    ///     Registers <typeparamref name="TActor" /> as the spawn recipe for <paramref name="actorType" />,
    ///     staged into <see cref="SchemataActorOptions.Registrations" /> and applied to the
    ///     <see cref="IActorRegistry" /> singleton once it is built.
    /// </summary>
    /// <typeparam name="TActor">The actor implementation to register.</typeparam>
    /// <param name="actorType">The route key, matched against <see cref="ActorId.Type" />.</param>
    /// <param name="args">The constructor arguments passed to <typeparamref name="TActor" /> when it is spawned.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataActorBuilder Register<TActor>(string actorType, params object[] args)
        where TActor : IActor {
        Services.Configure<SchemataActorOptions>(options =>
            options.Registrations.Add(new(actorType, new(typeof(TActor), args))));

        return this;
    }

    /// <summary>
    ///     Opens the opt-in state-persistence mechanism: an actor implementing
    ///     <see cref="IPersistentActor" /> has its state loaded before its first message and saved
    ///     after every turn that completes without throwing. Registers the internal
    ///     <see cref="ActorStateStore" /> - the only place this package adds it to the container
    ///     (R8). <c>IRepository&lt;SchemataActor&gt;</c> is resolved from it, not registered by it:
    ///     the application must register that repository itself.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataActorBuilder UsePersistence() {
        Services.TryAddScoped<ActorStateStore>();

        return this;
    }
}

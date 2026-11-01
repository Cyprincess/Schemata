using System;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     Creates the process actor system from the application services. The in-process runtime is
///     the default factory; a host that hosts actors differently registers its own factory for the
///     actor entity type it serves.
/// </summary>
public interface IActorSystemFactory
{
    /// <summary>Creates the actor system over <paramref name="services" />.</summary>
    /// <param name="services">The application services.</param>
    /// <returns>The actor system the process hosts.</returns>
    IActorSystem Create(IServiceProvider services);
}

using System.Threading.Tasks;

namespace Schemata.Actor.Skeleton;

/// <summary>The registry and lifecycle host for every actor instance in a process.</summary>
public interface IActorSystem
{
    /// <summary>Spawns and registers a new actor instance under <paramref name="id" />.</summary>
    /// <param name="id">The identity to register the new instance under.</param>
    /// <param name="props">The type and constructor arguments of the actor to spawn.</param>
    /// <returns>A reference to the newly spawned actor.</returns>
    Task<IActorRef> SpawnAsync(ActorId id, Props props);

    /// <summary>
    ///     Resolves a reference to the actor identified by <paramref name="id" />, spawning it
    ///     first if it does not already exist.
    /// </summary>
    /// <remarks>
    ///     Spawn-if-absent resolves <see cref="ActorId.Type" /> through <see cref="IActorRegistry" />;
    ///     when the type has no registration, this throws a clear exception rather than
    ///     silently constructing some default actor.
    /// </remarks>
    /// <param name="id">The identity to resolve.</param>
    /// <returns>A reference to the existing or newly spawned actor.</returns>
    Task<IActorRef> GetAsync(ActorId id);

    /// <summary>Stops and removes the actor identified by <paramref name="id" />, if it exists.</summary>
    /// <param name="id">The identity to stop.</param>
    Task StopAsync(ActorId id);
}

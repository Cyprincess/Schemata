namespace Schemata.Actor.Skeleton;

/// <summary>Maps an <see cref="ActorId.Type" /> route key to the <see cref="Props" /> used to spawn it.</summary>
public interface IActorRegistry
{
    /// <summary>Registers <paramref name="props" /> as the spawn recipe for <paramref name="actorType" />.</summary>
    /// <param name="actorType">The route key, matched against <see cref="ActorId.Type" />.</param>
    /// <param name="props">The spawn recipe to associate with <paramref name="actorType" />.</param>
    void Register(string actorType, Props props);

    /// <summary>Attempts to resolve the registered spawn recipe for <paramref name="actorType" />.</summary>
    /// <param name="actorType">The route key, matched against <see cref="ActorId.Type" />.</param>
    /// <param name="props">The registered recipe, when found.</param>
    /// <returns><see langword="true" /> when a recipe is registered for <paramref name="actorType" />.</returns>
    bool TryResolve(string actorType, out Props props);
}

using System;
using System.Collections.Concurrent;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>In-memory <see cref="IActorRegistry" /> mapping an <see cref="ActorId.Type" /> route key to its spawn <see cref="Props" />.</summary>
public sealed class ActorRegistry : IActorRegistry
{
    private readonly ConcurrentDictionary<string, Props> _recipes = new(StringComparer.Ordinal);

    #region IActorRegistry Members

    public void Register(string actorType, Props props) {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentNullException.ThrowIfNull(props);

        _recipes[actorType] = props;
    }

    public bool TryResolve(string actorType, out Props props) {
        if (_recipes.TryGetValue(actorType, out var found)) {
            props = found;
            return true;
        }

        props = null!;
        return false;
    }

    #endregion
}

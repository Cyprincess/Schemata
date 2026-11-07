using System;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

internal sealed class ActorTurnIdentity
{
    private ActorId? _actor;
    private IActorSystem? _system;

    internal void Bind(ActorId actor, IActorSystem system) {
        if (_actor is not null) throw new InvalidOperationException("The actor turn identity is already bound.");
        _actor = actor;
        _system = system;
    }

    internal bool IsCurrent(ActorId target, IActorSystem system) =>
        _actor == target && ReferenceEquals(_system, system);
}

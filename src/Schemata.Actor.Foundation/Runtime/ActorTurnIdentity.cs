using System;
using System.Threading;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

internal sealed class ActorTurnIdentity : IDisposable
{
    private static readonly AsyncLocal<ActorTurnIdentity?> Ambient = new();
    private readonly ActorTurnIdentity? _previous;
    private readonly ActorId _actor;
    private int _active = 1;

    internal ActorTurnIdentity(ActorId actor) {
        _actor = actor;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    internal static bool IsCurrent(ActorId target) => Ambient.Value is { } turn
        && Volatile.Read(ref turn._active) != 0 && turn._actor == target;

    internal static void RejectSelfAsk(ActorId target) {
        if (Ambient.Value is { } turn && Volatile.Read(ref turn._active) != 0 && turn._actor == target) {
            throw new InvalidOperationException($"Actor '{target}' cannot await an Ask to itself from its own callback.");
        }
    }

    public void Dispose() {
        Volatile.Write(ref _active, 0);
        Ambient.Value = _previous;
    }
}

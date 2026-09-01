using System.Threading;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Records every <see cref="IActor.OnStoppedAsync" /> notification into a shared, externally observable count.</summary>
public sealed class StopNotifications
{
    private int _count;

    public int Count => _count;

    public void RecordStopped() => Interlocked.Increment(ref _count);
}
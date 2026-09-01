using System.Threading;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>A thread-safe counter, incremented once per real construction - reveals a "losing" concurrent factory invocation that a caller never sees but that still ran.</summary>
public sealed class SharedCounter
{
    private int _count;

    public int Count => _count;

    public void Increment() => Interlocked.Increment(ref _count);
}
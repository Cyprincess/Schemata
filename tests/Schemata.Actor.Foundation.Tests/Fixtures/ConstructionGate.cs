using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Lets a test deterministically hold an actor's own constructor open mid-construction, on a synchronous handshake since a constructor cannot itself be awaited.</summary>
public sealed class ConstructionGate
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim       _release = new(initialState: false);

    /// <summary>Completes once a constructor has called <see cref="WaitForRelease" /> and is now blocked on it.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Called from inside the constructor: signals entry, then synchronously blocks until <see cref="Release" />.</summary>
    public void WaitForRelease() {
        _entered.TrySetResult(true);
        _release.Wait();
    }

    /// <summary>Called from the test: lets a constructor blocked on <see cref="WaitForRelease" /> continue.</summary>
    public void Release() => _release.Set();
}
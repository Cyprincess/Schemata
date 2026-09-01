using System.Threading.Tasks;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Lets a test deterministically hold a turn open mid-execution, then release it on demand.</summary>
public sealed class ManualGate
{
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a turn has called <see cref="WaitForReleaseAsync" /> and is now blocked on it.</summary>
    public Task Started => _started.Task;

    /// <summary>Called from inside a turn: signals that the turn has started, then blocks until <see cref="Release" />.</summary>
    public Task WaitForReleaseAsync() {
        _started.TrySetResult(true);
        return _release.Task;
    }

    /// <summary>Called from the test: lets a turn blocked on <see cref="WaitForReleaseAsync" /> continue.</summary>
    public void Release() => _release.TrySetResult(true);
}
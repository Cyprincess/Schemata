using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Records construction, lifecycle callbacks and disposal of every instance a spawn creates,
///     so a test can assert exactly-once ownership and ordering across supervision replacement.
/// </summary>
public sealed class DisposalTracker
{
    private readonly object                                    _gate    = new();
    private readonly List<string>                              _events  = [];
    private readonly Dictionary<string, TaskCompletionSource<bool>> _waiters = new();
    private          int                                       _constructed;

    public IReadOnlyList<string> Events {
        get {
            lock (_gate) {
                return _events.ToArray();
            }
        }
    }

    /// <summary>Assigns the next instance index; called from the actor's constructor.</summary>
    public int Register() {
        lock (_gate) {
            var index = _constructed++;
            Add($"Constructed[{index}]");
            return index;
        }
    }

    public void Record(int index, string name) {
        lock (_gate) {
            Add($"{name}[{index}]");
        }
    }

    public int Count(string name, int index) {
        lock (_gate) {
            return _events.Count(e => e == $"{name}[{index}]");
        }
    }

    /// <summary>Completes once <paramref name="entry" /> (e.g. <c>Disposed[1]</c>) has been recorded.</summary>
    public Task WaitForAsync(string entry) {
        lock (_gate) {
            if (_events.Contains(entry)) return Task.CompletedTask;
            if (!_waiters.TryGetValue(entry, out var waiter)) {
                _waiters[entry] = waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return waiter.Task;
        }
    }

    private void Add(string entry) {
        _events.Add(entry);
        if (_waiters.Remove(entry, out var waiter)) waiter.TrySetResult(true);
    }
}

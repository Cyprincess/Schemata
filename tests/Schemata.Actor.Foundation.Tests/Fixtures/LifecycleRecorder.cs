using System;
using System.Collections.Generic;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Records every lifecycle callback invocation with entry/exit tracking, so a test can assert
///     no two callbacks were ever active at the same time.
/// </summary>
public sealed class LifecycleRecorder
{
    private readonly object       _gate   = new();
    private readonly List<string> _events = [];
    private          int          _active;

    public IReadOnlyList<string> Events {
        get {
            lock (_gate) {
                return _events.ToArray();
            }
        }
    }

    public int MaxConcurrent { get; private set; }

    /// <summary>Marks entry into a callback; disposing the result marks its exit.</summary>
    public IDisposable Enter(string name) {
        lock (_gate) {
            _active++;
            MaxConcurrent = Math.Max(MaxConcurrent, _active);
            _events.Add(name);
        }

        return new ExitScope(this);
    }

    private sealed class ExitScope(LifecycleRecorder recorder) : IDisposable
    {
        public void Dispose() {
            lock (recorder._gate) {
                recorder._active--;
            }
        }
    }
}
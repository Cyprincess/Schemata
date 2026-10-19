using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Schemata.Scheduling.Skeleton.Services;

namespace Schemata.Scheduling.Foundation.Runtime;

/// <summary>Thread-safe <see cref="IActiveExecutionRegistry" /> for in-process execution cancellation.</summary>
public sealed class ActiveExecutionRegistry : IActiveExecutionRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);

    public IDisposable Register(string executionName, CancellationTokenSource source) {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionName);
        ArgumentNullException.ThrowIfNull(source);

        if (!_active.TryAdd(executionName, source)) {
            throw new InvalidOperationException($"Execution '{executionName}' already has a local owner.");
        }

        return new Registration(this, executionName, source);
    }

    public bool TryRemove(string executionName, out CancellationTokenSource? source) {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionName);

        if (_active.TryRemove(executionName, out var removed)) {
            source = removed;
            return true;
        }

        source = null;
        return false;
    }

    public bool Cancel(string executionName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionName);

        if (!_active.TryGetValue(executionName, out var source)) {
            return false;
        }

        try {
            source.Cancel();
            return true;
        } catch (ObjectDisposedException) {
            return false;
        }
    }

    private sealed class Registration(
        ActiveExecutionRegistry registry,
        string                  executionName,
        CancellationTokenSource source
    ) : IDisposable
    {
        private int _released;

        public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) != 0) {
                return;
            }

            if (registry._active.TryRemove(new KeyValuePair<string, CancellationTokenSource>(executionName, source))) {
                source.Dispose();
            }
        }
    }
}

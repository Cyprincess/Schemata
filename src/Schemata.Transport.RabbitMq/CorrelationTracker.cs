using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Transport.RabbitMq;

/// <summary>Tracks in-flight RabbitMQ request/response calls by correlation identifier.</summary>
public sealed class CorrelationTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly TimeProvider                          _timeProvider;

    /// <summary>Initializes a tracker, optionally over a test-controlled clock.</summary>
    public CorrelationTracker(TimeProvider? timeProvider = null) {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    #region IDisposable Members

    public void Dispose() {
        // Only the path that wins the dictionary removal releases a pending entry's CTS; a racing
        // timeout continuation finds the entry gone and never touches the transferred resources.
        foreach (var kvp in _pending) {
            if (!_pending.TryRemove(kvp.Key, out var pending)) continue;
            pending.Timeout.Cancel();
            pending.Timeout.Dispose();
            pending.Source.TrySetCanceled();
        }
    }

    #endregion

    /// <summary>Registers a pending response and returns the broker correlation identifier.</summary>
    public string Track<TResponse>(TaskCompletionSource<TResponse> tcs, TimeSpan timeout) {
        // A correlation id lives only until its reply arrives and is never persisted or ordered,
        // so an unsequenced GUID is sufficient here.
        var correlationId = Guid.NewGuid().ToString("n");
        var wrapper       = new TaskCompletionSource<object?>();
        var cts           = new CancellationTokenSource();

        _pending[correlationId] = new(wrapper, cts);

        _ = Task.Delay(timeout, _timeProvider, cts.Token).ContinueWith(task => {
            // A canceled delay means a reply, an abandon, or disposal removed the entry first and
            // owns the CTS; the losing side of the race never touches the transferred resources.
            if (task.IsCanceled) {
                return;
            }

            // The timeout releases the entry only together with the dictionary removal it wins.
            if (_pending.TryRemove(correlationId, out var pending)) {
                pending.Source.TrySetException(new TimeoutException("Request timed out waiting for response."));
                pending.Timeout.Dispose();
            }
        }, TaskScheduler.Default);

        _ = wrapper.Task.ContinueWith(t => {
            switch (t) {
                case { IsCompletedSuccessfully: true }:
                    if (t.Result is TResponse response) {
                        tcs.TrySetResult(response);
                    } else if (t.Result is null) {
                        tcs.TrySetResult(default!);
                    } else {
                        tcs.TrySetException(new InvalidCastException(
                                                $"Response type mismatch: expected '{
                                                    typeof(TResponse).FullName
                                                }', got '{
                                                    t.Result.GetType().FullName
                                                }'."));
                    }

                    break;
                case { IsFaulted: true, Exception: not null }:
                    tcs.TrySetException(t.Exception.InnerException ?? t.Exception);
                    break;
                default:
                {
                    if (t.IsCanceled) {
                        tcs.TrySetCanceled();
                    }

                    break;
                }
            }
        }, TaskScheduler.Default);

        return correlationId;
    }

    /// <summary>Completes the tracked request for <paramref name="correlationId" /> with a response payload.</summary>
    public bool Complete(string correlationId, object? result) {
        if (!_pending.TryRemove(correlationId, out var pending)) {
            return false;
        }

        // Release the pending timeout's delay timer.
        pending.Timeout.Cancel();
        pending.Timeout.Dispose();
        pending.Source.TrySetResult(result);

        return true;
    }

    /// <summary>Fails the tracked request for <paramref name="correlationId" /> with a remote error.</summary>
    public bool Fail(string correlationId, Exception exception) {
        if (!_pending.TryRemove(correlationId, out var pending)) {
            return false;
        }

        // Release the pending timeout's delay timer.
        pending.Timeout.Cancel();
        pending.Timeout.Dispose();
        pending.Source.TrySetException(exception);

        return true;
    }

    /// <summary>Cancels and removes a pending request that the caller abandoned.</summary>
    public bool Abandon(string correlationId) {
        if (!_pending.TryRemove(correlationId, out var pending)) {
            return false;
        }

        // Release the pending timeout's delay timer.
        pending.Timeout.Cancel();
        pending.Timeout.Dispose();
        pending.Source.TrySetCanceled();

        return true;
    }

    private sealed record Pending(TaskCompletionSource<object?> Source, CancellationTokenSource Timeout);
}

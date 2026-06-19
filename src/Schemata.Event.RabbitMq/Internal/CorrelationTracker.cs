using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Common;

namespace Schemata.Event.RabbitMq.Internal;

/// <summary>Tracks in-flight RabbitMQ request/response calls by correlation identifier.</summary>
public sealed class CorrelationTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    #region IDisposable Members

    public void Dispose() {
        foreach (var kvp in _pending) {
            kvp.Value.Source.TrySetCanceled();
            kvp.Value.Timeout.Dispose();
        }

        _pending.Clear();
    }

    #endregion

    /// <summary>Registers a pending response and returns the broker correlation identifier.</summary>
    public string Track<TResponse>(TaskCompletionSource<TResponse> tcs, TimeSpan timeout) {
        var correlationId = Identifiers.NewUid().ToString("n");
        var wrapper       = new TaskCompletionSource<object?>();
        var cts           = new CancellationTokenSource();

        _pending[correlationId] = new(wrapper, cts);

        _ = Task.Delay(timeout, cts.Token).ContinueWith(task => {
            // Replies and disposal cancel the delay before it elapses; an elapsed timeout fails
            // the request if it wins the race to remove the entry.
            if (task.IsCanceled) {
                return;
            }

            if (_pending.TryRemove(correlationId, out _)) {
                wrapper.TrySetException(new TimeoutException("Request timed out waiting for response."));
            }

            cts.Dispose();
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

    private sealed record Pending(TaskCompletionSource<object?> Source, CancellationTokenSource Timeout);
}

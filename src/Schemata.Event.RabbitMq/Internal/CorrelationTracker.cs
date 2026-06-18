using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Common;

namespace Schemata.Event.RabbitMq.Internal;

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

    public string Track<TResponse>(TaskCompletionSource<TResponse> tcs, TimeSpan timeout) {
        var correlationId = Identifiers.NewUid().ToString("n");
        var wrapper       = new TaskCompletionSource<object?>();
        var cts           = new CancellationTokenSource();

        _pending[correlationId] = new(wrapper, cts);

        _ = Task.Delay(timeout, cts.Token).ContinueWith(task => {
            // A reply (or disposal) cancels the delay before it elapses; only an elapsed timeout
            // should fail the request, and only if it wins the race to remove the entry.
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

    public bool Complete(string correlationId, object? result) {
        if (!_pending.TryRemove(correlationId, out var pending)) {
            return false;
        }

        // Cancel the pending timeout so its delay timer is released instead of lingering.
        pending.Timeout.Cancel();
        pending.Timeout.Dispose();
        pending.Source.TrySetResult(result);

        return true;
    }

    private sealed record Pending(TaskCompletionSource<object?> Source, CancellationTokenSource Timeout);
}

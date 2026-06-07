using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Schemata.Event.RabbitMq.Internal;

public sealed class CorrelationTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object?>> _pending = new();

    #region IDisposable Members

    public void Dispose() {
        foreach (var kvp in _pending) {
            kvp.Value.TrySetCanceled();
        }

        _pending.Clear();
    }

    #endregion

    public string Track<TResponse>(TaskCompletionSource<TResponse> tcs, TimeSpan timeout) {
        var correlationId = Guid.NewGuid().ToString("n");
        var wrapper       = new TaskCompletionSource<object?>();

        _pending[correlationId] = wrapper;

        _ = Task.Delay(timeout).ContinueWith(task => {
            _pending.TryRemove(correlationId, out var _);
            wrapper.TrySetException(new TimeoutException("Request timed out waiting for response."));
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
        if (!_pending.TryRemove(correlationId, out var tcs)) {
            return false;
        }

        tcs.TrySetResult(result);

        return true;
    }
}

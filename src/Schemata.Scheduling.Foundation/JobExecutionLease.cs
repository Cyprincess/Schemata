using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Services;

namespace Schemata.Scheduling.Foundation;

internal sealed class JobExecutionLease<TJob, TExecution> : IAsyncDisposable
    where TJob : SchemataJob
    where TExecution : SchemataJobExecution
{
    private readonly JobExecutionScopeFactory _scopes;
    private readonly TExecution _execution;
    private readonly TimeProvider _time;
    private readonly TimeSpan _duration;
    private readonly IDisposable _registration;
    private readonly CancellationTokenSource _expiry;
    private readonly CancellationTokenSource _work;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly Task _renewal;
    private bool _settled;
    private int _lost;
    private long _confirmedExpiry;

    internal JobExecutionLease(JobExecutionScopeFactory scopes, TExecution execution, TimeSpan duration,
        TimeProvider time, IActiveExecutionRegistry executions, CancellationToken ct) {
        _scopes = scopes;
        _execution = execution;
        _duration = duration;
        _time = time;
        _confirmedExpiry = execution.LeaseExpireTime!.Value.Ticks;
        var remaining = execution.LeaseExpireTime!.Value - time.GetUtcNow().UtcDateTime;
        _expiry = new(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, time);
        _work = CancellationTokenSource.CreateLinkedTokenSource(ct, _expiry.Token);
        try {
            _registration = executions.Register(execution.Uid.ToString("n"), _work);
        } catch {
            _work.Dispose();
            _stop.Dispose();
            _writes.Dispose();
            _expiry.Dispose();
            throw;
        }
        _renewal = RenewAsync();
    }

    internal TExecution Execution => _execution;

    internal CancellationToken Token => _work.Token;

    internal void ThrowIfOwnershipLost() {
        if (_time.GetUtcNow().UtcDateTime.Ticks >= Interlocked.Read(ref _confirmedExpiry)) _work.Cancel();
        Token.ThrowIfCancellationRequested();
    }

    internal Task<bool> TransitionAsync(Func<IJobExecutionStore<TJob, TExecution>, Task<bool>> transition, CancellationToken ct = default) {
        return TransitionAsync((_, store, token) => transition(store), ct);
    }

    internal async Task<T> TransitionAsync<T>(
        Func<IServiceProvider, IJobExecutionStore<TJob, TExecution>, CancellationToken, Task<T>> transition,
        CancellationToken                                                                ct = default
    ) {
        await _writes.WaitAsync(CancellationToken.None);
        try {
            if (_settled || _expiry.IsCancellationRequested || Volatile.Read(ref _lost) != 0
                || _time.GetUtcNow().UtcDateTime.Ticks >= Interlocked.Read(ref _confirmedExpiry)) return default!;
            _settled = true;
            _expiry.CancelAfter(Timeout.InfiniteTimeSpan);
            _stop.Cancel();
            return await ExecuteInScopeAsync(
                (services, token) => transition(services, services.GetRequiredService<IJobExecutionStore<TJob, TExecution>>(), token), ct);
        } finally {
            _writes.Release();
        }
    }

    /// <summary>
    ///     Runs a follow-up settlement step in a fresh execution scope after a failed in-scope
    ///     attempt left the scope's repositories completed. Ownership was validated when the
    ///     transition started.
    /// </summary>
    internal Task<T> RecoverAsync<T>(
        Func<IServiceProvider, IJobExecutionStore<TJob, TExecution>, CancellationToken, Task<T>> work,
        CancellationToken                                                                ct = default
    ) {
        return ExecuteInScopeAsync((services, token) => work(services, services.GetRequiredService<IJobExecutionStore<TJob, TExecution>>(), token), ct);
    }

    private async Task<T> ExecuteInScopeAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken ct) {
        var          scope   = await _scopes(ct);
        IDisposable? entered = null;
        Exception?   failure = null;
        try {
            entered = scope.Scope.Enter();
            await scope.Scope.RestoreAsync(scope.Context, ct);
            return await work(scope.Scope.Services, ct);
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.Scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
    }

    internal async Task ConfirmAsync(CancellationToken ct) {
        await _writes.WaitAsync(ct);
        try {
            if (_settled) return;
            ct.ThrowIfCancellationRequested();
            ThrowIfOwnershipLost();
            var          scope   = await _scopes(ct);
            IDisposable? entered = null;
            Exception?   failure = null;
            try {
                entered = scope.Scope.Enter();
                await scope.Scope.RestoreAsync(scope.Context, ct);
                var store = scope.Scope.Services.GetRequiredService<IJobExecutionStore<TJob, TExecution>>();
                var expires = _time.GetUtcNow().UtcDateTime.Add(_duration);
                if (!await store.TryRenewLeaseAsync(_execution, expires, ct)) {
                    Interlocked.Exchange(ref _lost, 1);
                    await _work.CancelAsync();
                    return;
                }
                if (ct.IsCancellationRequested) return;
                var remaining = expires - _time.GetUtcNow().UtcDateTime;
                if (remaining <= TimeSpan.Zero) {
                    Interlocked.Exchange(ref _lost, 1);
                    await _work.CancelAsync();
                    return;
                }
                Interlocked.Exchange(ref _confirmedExpiry, expires.Ticks);
                _expiry.CancelAfter(remaining);
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                Exception? cleanup = null;
                try { await scope.Scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
                try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) {
                    if (failure is not null) throw new AggregateException(failure, cleanup);
                    ExceptionDispatchInfo.Capture(cleanup).Throw();
                }
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (OperationCanceledException error) {
            Interlocked.Exchange(ref _lost, 1);
            await _work.CancelAsync();
            throw new InvalidOperationException("Execution lease confirmation was independently cancelled.", error);
        } catch {
            Interlocked.Exchange(ref _lost, 1);
            await _work.CancelAsync();
            throw;
        } finally {
            _writes.Release();
        }
    }

    private async Task RenewAsync() {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, Token);
        try {
            while (true) {
                await Task.Delay(TimeSpan.FromTicks(Math.Max(1, _duration.Ticks / 2)), _time, bounded.Token);
                await ConfirmAsync(bounded.Token);
            }
        } catch (OperationCanceledException) when (bounded.IsCancellationRequested) {
            return;
        } catch (OperationCanceledException error) {
            Interlocked.Exchange(ref _lost, 1);
            await _work.CancelAsync();
            throw new InvalidOperationException("Execution lease renewal was independently cancelled.", error);
        } catch {
            Interlocked.Exchange(ref _lost, 1);
            await _work.CancelAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync() {
        await _stop.CancelAsync();
        try {
            await _renewal;
        } finally {
            _registration.Dispose();
            _stop.Dispose();
            _expiry.Dispose();
            _writes.Dispose();
        }
    }
}

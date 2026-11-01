using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Services;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Polling implementation of <see cref="IOperationService" /> backed by persisted execution rows.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class DefaultOperationService<TJob, TExecution> : IOperationService
    where TJob : SchemataJob
    where TExecution : SchemataJobExecution, new()
{
    private readonly IServiceProvider _services;
    private readonly IMessageExecutionScopeFactory _scopes;
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduler _scheduler;
    private readonly IActiveExecutionRegistry _executions;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service with scoped execution storage and scheduler coordination.</summary>
    /// <param name="services">Provider used to capture and restore operation execution context.</param>
    /// <param name="options">Scheduling options that configure the polling interval.</param>
    /// <param name="scheduler">Scheduler used to remove cancelled jobs.</param>
    /// <param name="time">Clock used for terminal execution timestamps.</param>
    public DefaultOperationService(
        IServiceProvider                     services,
        IOptions<SchemataSchedulingOptions> options,
        IScheduler                           scheduler,
        TimeProvider?                        time = null
    ) {
        _services  = services;
        _scopes    = services.GetRequiredService<IMessageExecutionScopeFactory>();
        _options   = options;
        _scheduler = scheduler;
        _executions = services.GetRequiredService<IActiveExecutionRegistry>();
        _time      = time ?? TimeProvider.System;
    }

    public async ValueTask<Operation> GetAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var context = await MessageContexts.CaptureAsync(_services, ct);
        var scope = await _scopes.CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var executions = scope.Services.GetRequiredService<IRepository<TExecution>>();
            var execution = await FindAsync(executions, operation, ct);
            return OperationMapper.FromExecution(execution);
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
    }

    public async ValueTask<Operation> WaitAsync(string operation, CancellationToken ct = default) {
        while (true) {
            ct.ThrowIfCancellationRequested();

            var current = await GetAsync(operation, ct);
            if (current.Done) {
                return current;
            }

            await Task.Delay(PollInterval, _time, ct);
        }
    }

    public async ValueTask<Operation> CancelAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var context = await MessageContexts.CaptureAsync(_services, ct);
        TExecution execution;
        var lookup = await _scopes.CreateAsync(context, ct);
        IDisposable? lookupEntry = null;
        Exception? lookupFailure = null;
        try {
            lookupEntry = lookup.Enter();
            await lookup.RestoreAsync(context, ct);
            execution = await FindAsync(lookup.Services.GetRequiredService<IRepository<TExecution>>(), operation, ct);
        } catch (Exception error) {
            lookupFailure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await lookup.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { lookupEntry?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (lookupFailure is not null) throw new AggregateException(lookupFailure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
        if (execution.State.IsTerminal()) {
            throw new FailedPreconditionException(SchemataResources.OPERATION_ALREADY_FINISHED,
                new Dictionary<string, string?> { ["name"] = execution.CanonicalName });
        }
        context = new MessageContext(execution.MessageContext ?? new());
        for (var attempt = 0; attempt < 2; attempt++) {
            var selected = await _scopes.CreateAsync(context, ct);
            IDisposable? entered = null;
            Exception? failure = null;
            try {
                entered = selected.Enter();
                await selected.RestoreAsync(context, ct);
                if (attempt == 0) {
                    if (execution.State == ExecutionState.Pending && !string.IsNullOrEmpty(execution.Job)) {
                        await _scheduler.UnscheduleAsync(execution.Job, ct);
                        return await GetAsync(operation, ct);
                    }
                    _executions.Cancel(execution.Uid.ToString("n"));
                } else {
                    execution = await FindAsync(selected.Services.GetRequiredService<IRepository<TExecution>>(), operation, ct);
                    if (execution.State.IsTerminal()) return OperationMapper.FromExecution(execution);
                }
                execution.State = ExecutionState.Cancelled;
                execution.EndTime = _time.GetUtcNow().UtcDateTime;
                if (await selected.Services.GetRequiredService<IJobExecutionStore<TJob, TExecution>>().TrySettleAsync(execution, ct))
                    return OperationMapper.FromExecution(execution);
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                Exception? cleanup = null;
                try { await selected.DisposeAsync(); } catch (Exception error) { cleanup = error; }
                try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) {
                    if (failure is not null) throw new AggregateException(failure, cleanup);
                    ExceptionDispatchInfo.Capture(cleanup).Throw();
                }
            }
        }
        var winner = await GetAsync(operation, ct);
        if (winner.Done) return winner;
        throw new AbortedException("Operation cancellation lost its concurrent transition.");
    }

    public async ValueTask<Operation> ExecuteAsync(
        string method,
        Func<Operation, CancellationToken, ValueTask<string?>> execute,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(execute);

        var duration = _options.Value.ExecutionLease;
        if (duration <= TimeSpan.Zero) throw new InvalidOperationException("Execution lease must be greater than zero.");
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var execution = new TExecution {
            MessageContext = new(context.Items),
            Method    = method,
            State     = ExecutionState.Running,
            StartTime = _time.GetUtcNow().UtcDateTime,
            LeaseExpireTime = _time.GetUtcNow().UtcDateTime.Add(duration),
        };
        var creation = await _scopes.CreateAsync(context, ct);
        IDisposable? creationEntry = null;
        Exception? creationFailure = null;
        JobExecutionLease<TJob, TExecution>? lease = null;
        try {
            creationEntry = creation.Enter();
            await creation.RestoreAsync(context, ct);
            var mutation = creation.Services.GetRequiredService<IResourceMutation<TExecution>>();
            if (await mutation.CreateAsync(execution, null, ct) != MutationResult.Applied)
                throw new InvalidOperationException("Inline execution creation was not applied.");
            lease = new JobExecutionLease<TJob, TExecution>(JobExecutionScopes.Messaging(_scopes, context), execution, duration, _time, _executions, ct);
        } catch (Exception creationError) {
            creationFailure = creationError;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await creation.DisposeAsync(); } catch (Exception disposeError) { cleanup = disposeError; }
            try { creationEntry?.Dispose(); } catch (Exception entryError) { cleanup = cleanup is null ? entryError : new AggregateException(cleanup, entryError); }
            if (cleanup is not null) {
                if (lease is not null) {
                    try { await lease.DisposeAsync(); } catch (Exception leaseError) { cleanup = new AggregateException(cleanup, leaseError); }
                }
                if (creationFailure is not null) throw new AggregateException(creationFailure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
        await using var owned = lease!;
        var state = ExecutionState.Succeeded;
        string? output = null;
        string? error = null;
        MessageExecutionScope? scope = null;
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            owned.ThrowIfOwnershipLost();
            scope = await _scopes.CreateAsync(context, owned.Token);
            entered = scope.Enter();
            await scope.RestoreAsync(context, owned.Token);
            owned.ThrowIfOwnershipLost();
            await owned.ConfirmAsync(owned.Token);
            owned.ThrowIfOwnershipLost();
            output = await execute(OperationMapper.FromExecution(execution), owned.Token);
            owned.ThrowIfOwnershipLost();
        } catch (OperationCanceledException exception) when (owned.Token.IsCancellationRequested) {
            failure = exception;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await owned.TransitionAsync(store => {
                execution.State = ExecutionState.Cancelled;
                execution.EndTime = _time.GetUtcNow().UtcDateTime;
                return store.TrySettleAsync(execution, cleanup.Token);
            }, cleanup.Token);
            throw;
        } catch (Exception exception) when (owned.Token.IsCancellationRequested) {
            failure = exception;
            throw;
        } catch (Exception exception) {
            failure = exception;
            state = ExecutionState.Failed;
            error = exception.Message;
        } finally {
            Exception? cleanup = null;
            if (scope is not null) {
                try { await scope.DisposeAsync(); } catch (Exception exception) { cleanup = exception; }
            }
            try { entered?.Dispose(); } catch (Exception exception) { cleanup = cleanup is null ? exception : new AggregateException(cleanup, exception); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (!await owned.TransitionAsync(store => {
            execution.State = state;
            execution.Output = output;
            execution.RecentError = error;
            execution.EndTime = _time.GetUtcNow().UtcDateTime;
            return store.TrySettleAsync(execution, completion.Token);
        }, completion.Token)) {
            var winner = await GetAsync(execution.CanonicalName!, completion.Token);
            if (winner.Done) return winner;
            throw new AbortedException("Inline operation lost its terminal transition.");
        }
        return OperationMapper.FromExecution(execution);
    }


    private TimeSpan PollInterval {
        get {
            var interval = _options.Value.OperationPollInterval;
            return interval > TimeSpan.Zero
                ? interval
                : throw new InvalidOperationException("Operation poll interval must be greater than zero.");
        }
    }

    private static async ValueTask<TExecution> FindAsync(
        IRepository<TExecution> executions,
        string                  operation,
        CancellationToken       ct
    ) {
        var execution = await executions.FirstOrDefaultAsync<TExecution>(
            query => query.Where(e => e.CanonicalName == operation || e.Name == operation), ct);

        return execution ?? throw new NotFoundException(
            SchemataResources.OPERATION_NOT_FOUND,
            new Dictionary<string, string?> { ["name"] = operation });
    }
}

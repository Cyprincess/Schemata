using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IMessageExecutionScopeRunner _runner;
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
        IServiceProvider services,
        IOptions<SchemataSchedulingOptions> options,
        IScheduler scheduler,
        TimeProvider? time = null
    ) {
        _services = services;
        _scopes = services.GetRequiredService<IMessageExecutionScopeFactory>();
        _runner = services.GetRequiredService<IMessageExecutionScopeRunner>();
        _options = options;
        _scheduler = scheduler;
        _executions = services.GetRequiredService<IActiveExecutionRegistry>();
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<Operation> GetAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var context = await MessageContexts.CaptureAsync(_services, ct);
        return await _runner.RunAsync(context, async (services, token) => {
            var executions = services.GetRequiredService<IRepository<TExecution>>();
            var execution = await FindAsync(executions, operation, token);
            return OperationMapper.FromExecution(execution);
        }, ct);
    }

    public async ValueTask<Operation> WaitAsync(string operation, CancellationToken ct = default) {
        while (true) {
            ct.ThrowIfCancellationRequested();
            var current = await GetAsync(operation, ct);
            if (current.Done) return current;
            await Task.Delay(PollInterval, _time, ct);
        }
    }

    public async ValueTask<Operation> CancelAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var execution = await _runner.RunAsync(context, async (services, token) =>
            await FindAsync(services.GetRequiredService<IRepository<TExecution>>(), operation, token), ct);
        if (execution.State.IsTerminal()) {
            throw new FailedPreconditionException(SchemataResources.OPERATION_ALREADY_FINISHED,
                new Dictionary<string, string?> { ["name"] = execution.CanonicalName });
        }
        context = new MessageContext(execution.MessageContext ?? new());
        for (var attempt = 0; attempt < 2; attempt++) {
            var result = await _runner.RunAsync<Operation?>(context, async (services, token) => {
                if (attempt == 0) {
                    if (execution.State == ExecutionState.Pending && !string.IsNullOrEmpty(execution.Job)) {
                        await _scheduler.UnscheduleAsync(execution.Job, token);
                        return await GetAsync(operation, token);
                    }
                    _executions.Cancel(execution.Uid.ToString("n"));
                } else {
                    execution = await FindAsync(services.GetRequiredService<IRepository<TExecution>>(), operation, token);
                    if (execution.State.IsTerminal()) return OperationMapper.FromExecution(execution);
                }
                execution.State = ExecutionState.Cancelled;
                execution.EndTime = _time.GetUtcNow().UtcDateTime;
                if (await services.GetRequiredService<IJobExecutionStore<TJob, TExecution>>().TrySettleAsync(execution, token)) {
                    return OperationMapper.FromExecution(execution);
                }
                return null;
            }, ct);
            if (result is not null) return result;
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
            Method = method,
            State = ExecutionState.Running,
            StartTime = _time.GetUtcNow().UtcDateTime,
            LeaseExpireTime = _time.GetUtcNow().UtcDateTime.Add(duration),
        };
        JobExecutionLease<TJob, TExecution>? lease = null;
        try {
            await _runner.RunAsync(context, (services, token) => {
                var mutation = services.GetRequiredService<IResourceMutation<TExecution>>();
                return CreateLeaseAsync(mutation, token);
            }, ct);
        } catch (Exception creationFailure) {
            if (lease is null) throw;
            try {
                await lease.DisposeAsync();
            } catch (Exception cleanup) {
                throw new AggregateException(creationFailure, cleanup);
            }
            throw;
        }

        await using var owned = lease ?? throw new InvalidOperationException("Inline execution lease was not created.");
        var state = ExecutionState.Succeeded;
        string? output;
        string? error = null;
        try {
            owned.ThrowIfOwnershipLost();
            output = await _runner.RunAsync(context, async (_, token) => {
                owned.ThrowIfOwnershipLost();
                await owned.ConfirmAsync(token);
                owned.ThrowIfOwnershipLost();
                var result = await execute(OperationMapper.FromExecution(execution), token);
                owned.ThrowIfOwnershipLost();
                return result;
            }, owned.Token);
        } catch (OperationCanceledException) when (owned.Token.IsCancellationRequested) {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await owned.TransitionAsync(store => {
                execution.State = ExecutionState.Cancelled;
                execution.EndTime = _time.GetUtcNow().UtcDateTime;
                return store.TrySettleAsync(execution, cleanup.Token);
            }, cleanup.Token);
            throw;
        } catch (Exception) when (owned.Token.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            state = ExecutionState.Failed;
            error = exception.Message;
            output = null;
        }
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (!await owned.TransitionAsync(store => {
            execution.State = state;
            execution.Output = output;
            execution.RecentError = error;
            execution.EndTime = _time.GetUtcNow().UtcDateTime;
            return store.TrySettleAsync(execution, completion.Token);
        }, completion.Token)) {
            var canonical = execution.CanonicalName
                         ?? throw new InvalidOperationException("Inline execution has no canonical name.");
            var winner = await GetAsync(canonical, completion.Token);
            if (winner.Done) return winner;
            throw new AbortedException("Inline operation lost its terminal transition.");
        }
        return OperationMapper.FromExecution(execution);

        async ValueTask<bool> CreateLeaseAsync(IResourceMutation<TExecution> mutation, CancellationToken token) {
            if (await mutation.CreateAsync(execution, null, token) != MutationResult.Applied) {
                throw new InvalidOperationException("Inline execution creation was not applied.");
            }
            lease = new JobExecutionLease<TJob, TExecution>(
                JobExecutionScopes.Messaging(_scopes, context), execution, duration, _time, _executions, token);
            return true;
        }
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
        string operation,
        CancellationToken ct
    ) {
        var execution = await executions.FirstOrDefaultAsync<TExecution>(
            query => query.Where(e => e.CanonicalName == operation || e.Name == operation), ct);

        return execution ?? throw new NotFoundException(
            SchemataResources.OPERATION_NOT_FOUND,
            new Dictionary<string, string?> { ["name"] = operation });
    }
}

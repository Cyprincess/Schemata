using System;
using System.Collections.Concurrent;
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
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Polling implementation of <see cref="IOperationService" /> backed by persisted execution rows.
/// </summary>
public sealed class DefaultOperationService : IOperationService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduler _scheduler;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service with scoped execution storage and scheduler coordination.</summary>
    /// <param name="scopes">Factory used to resolve a fresh execution repository for each operation.</param>
    /// <param name="options">Scheduling options that configure the polling interval.</param>
    /// <param name="scheduler">Scheduler used to remove cancelled jobs.</param>
    /// <param name="time">Clock used for terminal execution timestamps.</param>
    public DefaultOperationService(
        IServiceScopeFactory                 scopes,
        IOptions<SchemataSchedulingOptions> options,
        IScheduler                           scheduler,
        TimeProvider?                        time = null
    ) {
        _scopes    = scopes;
        _options   = options;
        _scheduler = scheduler;
        _time      = time ?? TimeProvider.System;
    }

    public async ValueTask<Operation> GetAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        await using var scope = _scopes.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var execution = await FindAsync(executions, operation, ct);

        return OperationMapper.FromExecution(execution);
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

        await using var scope = _scopes.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var execution = await FindAsync(executions, operation, ct);

        if (execution.State.IsTerminal()) {
            throw new FailedPreconditionException(
                SchemataResources.OPERATION_ALREADY_FINISHED,
                new Dictionary<string, string?> { ["name"] = execution.CanonicalName });
        }

        if (execution.State == ExecutionState.Pending && !string.IsNullOrEmpty(execution.Job)) {
            await _scheduler.UnscheduleAsync(execution.Job, ct);
        }

        if (execution.State == ExecutionState.Running) {
            var running = scope.ServiceProvider.GetService<ConcurrentDictionary<string, CancellationTokenSource>>();
            if (running is not null && running.TryGetValue(execution.Uid.ToString("n"), out var source)) {
                try {
                    source.Cancel();
                } catch (ObjectDisposedException) {
                    // The execution completed while its cancellation was being requested.
                }
            }
        }

        execution.State   = ExecutionState.Cancelled;
        execution.EndTime = _time.GetUtcNow().UtcDateTime;
        await executions.UpdateAsync(execution, ct);
        await executions.CommitAsync(ct);

        return OperationMapper.FromExecution(execution);
    }

    public async ValueTask<Operation> ExecuteAsync(
        string method,
        Func<Operation, CancellationToken, ValueTask<string?>> execute,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(execute);

        var execution = new SchemataJobExecution {
            Method    = method,
            State     = ExecutionState.Running,
            StartTime = _time.GetUtcNow().UtcDateTime,
        };
        await using (var scope = _scopes.CreateAsyncScope()) {
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await executions.AddAsync(execution, ct);
            await executions.CommitAsync(ct);
        }

        try {
            execution.Output = await execute(OperationMapper.FromExecution(execution), ct);
            ct.ThrowIfCancellationRequested();
            execution.State = ExecutionState.Succeeded;
        } catch (OperationCanceledException) {
            execution.State = ExecutionState.Cancelled;
            execution.EndTime = _time.GetUtcNow().UtcDateTime;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await PersistTerminalAsync(execution, cleanup.Token);
            throw;
        } catch (Exception exception) {
            execution.State = ExecutionState.Failed;
            execution.RecentError = exception.Message;
        }

        execution.EndTime = _time.GetUtcNow().UtcDateTime;
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await PersistTerminalAsync(execution, completion.Token);
        return OperationMapper.FromExecution(execution);
    }

    private async Task PersistTerminalAsync(SchemataJobExecution execution, CancellationToken ct) {
        await using var scope = _scopes.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        await executions.UpdateAsync(execution, ct);
        await executions.CommitAsync(ct);
    }

    private TimeSpan PollInterval {
        get {
            var interval = _options.Value.OperationPollInterval;
            return interval > TimeSpan.Zero
                ? interval
                : throw new InvalidOperationException("Operation poll interval must be greater than zero.");
        }
    }

    private static async ValueTask<SchemataJobExecution> FindAsync(
        IRepository<SchemataJobExecution> executions,
        string                            operation,
        CancellationToken                 ct
    ) {
        var execution = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
            query => query.Where(e => e.CanonicalName == operation || e.Name == operation), ct);

        return execution ?? throw new NotFoundException(message: $"Operation '{operation}' was not found.");
    }
}

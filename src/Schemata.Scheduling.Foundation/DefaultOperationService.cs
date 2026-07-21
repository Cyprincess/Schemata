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
using Schemata.Common;
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

    /// <inheritdoc />
    public async ValueTask<Operation> GetAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        await using var scope = _scopes.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var execution = await FindAsync(executions, operation, ct);

        return OperationMapper.FromExecution(execution);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async ValueTask<Operation> CancelAsync(string operation, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        await using var scope = _scopes.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var execution = await FindAsync(executions, operation, ct);

        if (IsTerminal(execution.State)) {
            throw new FailedPreconditionException(
                SchemataResources.OPERATION_ALREADY_FINISHED,
                new Dictionary<string, string?> { ["name"] = execution.CanonicalName });
        }

        if (!string.IsNullOrEmpty(execution.Job)) {
            await _scheduler.UnscheduleAsync(execution.Job, ct);
        }

        execution.State   = ExecutionState.Cancelled;
        execution.EndTime = _time.GetUtcNow().UtcDateTime;
        await executions.UpdateAsync(execution, ct);
        await executions.CommitAsync(ct);

        return OperationMapper.FromExecution(execution);
    }

    /// <inheritdoc />
    public async ValueTask<Operation> CreateTerminalAsync(
        string method,
        string? output,
        string? error,
        Guid? uid = null,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(method);

        var executionUid = uid ?? Identifiers.NewUid();
        var now = _time.GetUtcNow().UtcDateTime;
        var execution = new SchemataJobExecution {
            Uid           = executionUid,
            Name          = executionUid.ToString("n"),
            CanonicalName = $"operations/{executionUid:n}",
            Method        = method,
            State         = error is null ? ExecutionState.Succeeded : ExecutionState.Failed,
            StartTime     = now,
            EndTime       = now,
            Output        = output,
            RecentError   = error,
        };

        await using var scope = _scopes.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        await executions.AddAsync(execution, ct);
        await executions.CommitAsync(ct);

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

    private static async ValueTask<SchemataJobExecution> FindAsync(
        IRepository<SchemataJobExecution> executions,
        string                            operation,
        CancellationToken                 ct
    ) {
        var execution = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
            query => query.Where(e => e.CanonicalName == operation || e.Name == operation), ct);

        return execution ?? throw new NotFoundException(message: $"Operation '{operation}' was not found.");
    }

    private static bool IsTerminal(ExecutionState state) {
        return state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;
    }
}

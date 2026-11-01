using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

public sealed partial class DefaultScheduler<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task ScheduleAsync(TJob job, CancellationToken ct) {
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var scope = await _services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var dispatcher = scope.Services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<ScheduleJobRequest<TJob>, Unit>(new(job, null), ct);
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

    public async Task ScheduleAsync(TJob job, IReadOnlyDictionary<string, string?>? variables, CancellationToken ct) {
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var scope = await _services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var dispatcher = scope.Services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<ScheduleJobRequest<TJob>, Unit>(new(job, variables, ReplaceVariables: true), ct);
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

    public async Task UnscheduleAsync(string jobCanonical, CancellationToken ct) {
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var scope = await _services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var dispatcher = scope.Services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<UnscheduleJobRequest, Unit>(new(jobCanonical), ct);
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
}

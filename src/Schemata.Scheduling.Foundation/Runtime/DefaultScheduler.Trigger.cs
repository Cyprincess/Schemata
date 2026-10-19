using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

public sealed partial class DefaultScheduler<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task<SchemataJobExecution> TriggerAsync<TJobType>(JobContext context, CancellationToken ct)
        where TJobType : class, IScheduledJob {
        var message = await MessageContexts.CaptureAsync(_services, ct);
        var scope = await _services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(message, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(message, ct);
            var dispatcher = scope.Services.GetRequiredService<IRequestDispatcher>();
            return await dispatcher.SendAsync<TriggerJobRequest<TExecution>, TExecution>(
                new(context.Job ?? string.Empty, typeof(TJobType), context), ct);
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

    public async Task RescheduleAsync(TJob job, JobContext? preparedContext, CancellationToken ct) {
        var message = new MessageContext(job.MessageContext ?? new());
        var scope = await _services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(message, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(message, ct);
            var dispatcher = scope.Services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<RescheduleJobRequest<TJob>, Unit>(new(job, preparedContext), ct);
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

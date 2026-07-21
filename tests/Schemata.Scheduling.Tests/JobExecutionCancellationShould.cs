using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionCancellationShould
{
    [Fact]
    public async Task Cancel_Running_Dispatch_Cancels_Job_Token_And_Persists_Cancelled() {
        var execution = new SchemataJobExecution {
            Uid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "22222222222222222222222222222222", CanonicalName = "operations/22222222222222222222222222222222",
            JobKey = "jobs.blocking", State = ExecutionState.Pending, StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query, CancellationToken _) => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? query, CancellationToken _) => new ValueTask<SchemataJobExecution?>(query!(new[] { execution }.AsQueryable()).SingleOrDefault()));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<BlockingJob>("jobs.blocking");
        var job = new BlockingJob();
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                       It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<SchemataJob?>(null));
        var running = new ConcurrentDictionary<string, CancellationTokenSource>();
        var scheduler = new Mock<IScheduler>();
        var services = new ServiceCollection().AddSingleton(executions.Object).AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job).AddSingleton(running).AddSingleton(jobs.Object)
                                              .AddSingleton<IScheduler>(scheduler.Object).BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher(services);
        var operation = new DefaultOperationService(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SchemataSchedulingOptions()), scheduler.Object);

        var dispatch = dispatcher.DispatchPendingAsync(CancellationToken.None);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await operation.CancelAsync(execution.CanonicalName!, CancellationToken.None);
        await job.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await dispatch;

        Assert.Equal(ExecutionState.Cancelled, execution.State);
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class BlockingJob : IScheduledJob
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Started.SetResult();
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                Cancelled.SetResult();
                throw;
            }
        }
    }
}

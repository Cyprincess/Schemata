using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     Asserts <see cref="JobExecutionDispatcher{TJob,TExecution}" /> gives
///     <see cref="IJobExecutionAdvisor" /> an explicit <see cref="AdviceContext" /> bound to the
///     fired execution's own scope, so fires never share pipeline state.
/// </summary>
public class JobExecutionAdviceContextShould
{
    [Fact]
    public async Task Dispatch_GivesEachFire_ItsOwnScopeContext() {
        var first = new SchemataJobExecution {
            Uid = Guid.NewGuid(), JobKey = "jobs.context", State = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var second = new SchemataJobExecution {
            Uid = Guid.NewGuid(), JobKey = "jobs.context", State = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-2),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _, QueryOptions _) => ToAsync(query(new[] { first, second }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<ObservingJob>("jobs.context");
        var job = new ObservingJob();

        var observed = new List<AdviceContext>();
        var advisor = new Mock<IJobExecutionAdvisor>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
               .Callback<AdviceContext, JobContext, CancellationToken>((ctx, _, _) => {
                    lock (observed) observed.Add(ctx);
                })
               .ReturnsAsync(AdviseResult.Continue);

        await using var services = new ServiceCollection().AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                                         .AddSingleton(executions.Object)
                                                         .AddSingleton<IScheduledJobRegistry>(registry)
                                                         .AddSingleton(job)
                                                         .AddSingleton<IJobExecutionAdvisor>(advisor.Object)
                                                         .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                                         .AddSchemataScheduling()
                                                         .AddSchemataSchedulingRepositoryStore()
                                                         .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(2, observed.Count);
        Assert.NotSame(observed[0], observed[1]);
        Assert.NotSame(observed[0].ServiceProvider, observed[1].ServiceProvider);
        Assert.NotSame(services, observed[0].ServiceProvider);
        Assert.Equal(2, job.Runs);
    }

    private static IRepository<SchemataJob> EmptyJobRepository() {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(ValueTask.FromResult<SchemataJob?>(null));
        return repository.Object;
    }

    private static IUnitOfWork CommittingUnit() {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class ObservingJob : IScheduledJob
    {
        public int Runs { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Runs++;
            return Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     Asserts <see cref="JobExecutionDispatcher" /> establishes the <see cref="AdviceContext" /> it
///     builds for <see cref="IJobExecutionAdvisor" /> as the ambient context for the whole fire —
///     the advisor and the triggered job body both observe the same <see cref="AdviceContext.Current" />
///     instance, and it is cleared again once the fire completes.
/// </summary>
public class JobExecutionAmbientContextShould
{
    [Fact]
    public async Task Establish_TheAdviceContext_AsAmbient_ForTheAdvisorAndTheJobBody() {
        var execution = new SchemataJobExecution {
            Uid = Identifiers.NewUid(), JobKey = "jobs.ambient", State = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _) => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<AmbientObservingJob>("jobs.ambient");
        var job = new AmbientObservingJob();

        AdviceContext? observedByAdvisor = null;
        var advisor = new Mock<IJobExecutionAdvisor>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
               .Callback<AdviceContext, JobContext, CancellationToken>((ctx, _, _) => observedByAdvisor = AdviceContext.Current)
               .ReturnsAsync(AdviseResult.Continue);

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton(job)
                                               .AddSingleton<IJobExecutionAdvisor>(advisor.Object)
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        Assert.NotNull(observedByAdvisor);
        Assert.NotNull(job.ObservedDuringExecute);
        Assert.Same(observedByAdvisor, job.ObservedDuringExecute);
        Assert.Null(AdviceContext.Current);
    }

    private static IRepository<SchemataJob> EmptyJobRepository() {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<SchemataJob?>(null));
        return repository.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class AmbientObservingJob : IScheduledJob
    {
        public AdviceContext? ObservedDuringExecute { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            ObservedDuringExecute = AdviceContext.Current;
            return Task.CompletedTask;
        }
    }
}

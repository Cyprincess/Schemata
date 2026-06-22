using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionDispatcherShould
{
    [Fact]
    public async Task DispatchPendingAsync_MissingJobKey_MarksExecutionFailed() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/missing",
            JobKey    = "jobs.missing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(new DefaultScheduledJobRegistry())
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("jobs.missing", execution.RecentError);
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}

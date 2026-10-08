using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public sealed class RunJobHandlerShould
{
    [Fact]
    public async Task Run_Carries_The_Request_Principal_Into_The_Trigger_Context() {
        var job = new SchemataJob {
            CanonicalName = "jobs/sample",
            JobKey        = "sample-key",
        };
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.SuppressQuerySoftDelete()).Returns((IDisposable?)null);
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(job));

        var registry = new Mock<IScheduledJobRegistry>();
        registry.Setup(r => r.Resolve("sample-key")).Returns(typeof(SampleJob));

        JobContext? captured = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<SampleJob>(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback<JobContext, CancellationToken>((context, _) => captured = context)
                 .ReturnsAsync(new SchemataJobExecution { Job = job.CanonicalName, State = ExecutionState.Pending });

        var services = new ServiceCollection().AddSingleton(jobs.Object)
                                              .AddSingleton<SampleJob>()
                                              .BuildServiceProvider();
        var handler   = new RunJobHandler(scheduler.Object, services, registry.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        await handler.HandleAsync(new() { CanonicalName = "jobs/sample", Principal = principal }, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Same(principal, captured.Principal);
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}

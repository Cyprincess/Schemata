using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Scheduling.Tests;

public sealed class RunJobHandlerShould
{
    [Fact]
    public async Task Run_Carries_Job_Identity_And_Variables_Into_The_Trigger_Context() {
        var job = new SchemataJob {
            CanonicalName = "jobs/sample",
            JobKey        = "sample-key",
        };
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
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
        var advice = new AdviceContext(services);
        var handler = new RunJobHandler<SchemataJob>(scheduler.Object, services, registry.Object);

        await handler.HandleAsync(new() {
            CanonicalName = "jobs/sample",
            Variables     = new Dictionary<string, string?> { ["from"] = "caller" },
        }, advice, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("jobs/sample", captured.Job);
        Assert.Equal("caller", captured.Variables["from"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_Carries_Stored_Arguments_And_Variables_Into_The_Trigger_Context(bool withOverrides) {
        var job = new SchemataJob {
            CanonicalName = "jobs/sample",
            JobKey        = "sample-key",
            ArgsJson      = """{"count":3}""",
            Variables     = new Dictionary<string, string?> { ["tier"] = "gold", ["region"] = "eu" },
        };
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
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
        var advice = new AdviceContext(services);
        var handler = new RunJobHandler<SchemataJob>(scheduler.Object, services, registry.Object);

        await handler.HandleAsync(new() {
            CanonicalName = "jobs/sample",
            Variables     = withOverrides ? new Dictionary<string, string?> { ["region"] = "us" } : null,
        }, advice, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("""{"count":3}""", captured.ArgsJson);
        Assert.Equal("gold", captured.Variables["tier"]);
        Assert.Equal(withOverrides ? "us" : "eu", captured.Variables["region"]);
    }

    [Fact]
    public async Task Run_Rejects_Stored_Job_Without_Canonical_Identity() {
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(new SchemataJob { Name = "sample", JobKey = "sample-key" });
        using var services = new ServiceCollection().AddSingleton(jobs.Object).BuildServiceProvider();
        var advice = new AdviceContext(services);
        var scheduler = new Mock<IScheduler>();
        var handler = new RunJobHandler<SchemataJob>(scheduler.Object, services, Mock.Of<IScheduledJobRegistry>());
        await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.FailedPreconditionException>(() =>
            handler.HandleAsync(new() { CanonicalName = "sample" }, advice));
        scheduler.Verify(s => s.TriggerAsync<SampleJob>(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}

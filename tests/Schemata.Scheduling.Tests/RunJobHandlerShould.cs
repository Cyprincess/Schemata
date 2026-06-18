using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class RunJobHandlerShould
{
    [Fact]
    public async Task SchedulerFailure_SurfacesOriginal() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<SampleJob>(It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Throws(new InvalidOperationException("scheduler boom"));

        var services = new ServiceCollection().AddTransient<SampleJob>().BuildServiceProvider();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<SampleJob>();
        var handler  = new RunJobHandler(scheduler.Object, services, registry);
        var job      = new SchemataJob { CanonicalName = "jobs/x", JobKey = typeof(SampleJob).FullName };

        // The reflected dispatch wraps a synchronous scheduler failure in a
        // TargetInvocationException; the handler must surface the original.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.InvokeAsync(null, new(), job, null, CancellationToken.None).AsTask());

        Assert.Equal("scheduler boom", ex.Message);
    }

    private sealed class SampleJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            return Task.CompletedTask;
        }

        #endregion
    }
}

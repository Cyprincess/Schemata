using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class ScheduledJobServiceCollectionExtensionsShould
{
    [Fact]
    public void AddScheduledJob_RecordsKnownJobType() {
        var services = new ServiceCollection();

        services.AddScheduledJob<FakeJob>();

        Assert.Contains(Options(services).Jobs, j => j.JobType == typeof(FakeJob) && j.Schedule is null);
    }

    [Fact]
    public void AddScheduledJob_RegistersJobAsResolvable() {
        var services = new ServiceCollection();

        services.AddScheduledJob<FakeJob>();

        Assert.NotNull(services.BuildServiceProvider().GetService<FakeJob>());
    }

    [Fact]
    public void AddScheduledJob_IsIdempotent() {
        var services = new ServiceCollection();

        services.AddScheduledJob<FakeJob>();
        services.AddScheduledJob<FakeJob>();

        Assert.Single(Options(services).Jobs, j => j.JobType == typeof(FakeJob));
    }

    private static SchemataSchedulingOptions Options(IServiceCollection services) {
        return services.BuildServiceProvider().GetRequiredService<IOptions<SchemataSchedulingOptions>>().Value;
    }

    #region Nested type: FakeJob

    private sealed class FakeJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion
}

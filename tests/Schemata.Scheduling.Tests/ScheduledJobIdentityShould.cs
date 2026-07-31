using System;
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
using Schemata.Scheduling.Skeleton.Attributes;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class ScheduledJobIdentityShould
{
    private static readonly char[] Unaddressable = ['`', '[', ']', ',', '=', ' ', '/'];

    [Fact]
    public void DropAssemblyQualificationAndArity_FromAClosedGenericJobKey() {
        var key = new DefaultScheduledJobRegistry().ResolveKey(typeof(ProbeJob<ProbePayload>));

        Assert.Equal("Schemata.Scheduling.Tests.ProbeJob.ProbePayload", key);
    }

    [Fact]
    public void KeepAClosedGenericJobKeyAddressable() {
        var key = new DefaultScheduledJobRegistry().ResolveKey(typeof(ProbeJob<ProbePayload>))!;

        Assert.True(key.Length < 128, $"Derived key is {key.Length} characters: {key}");
        Assert.Equal(-1, key.IndexOfAny(Unaddressable));
    }

    [Fact]
    public void ResolveAClosedGenericJobBackFromItsDerivedKey() {
        var registry = new DefaultScheduledJobRegistry();
        var key      = registry.ResolveKey(typeof(ProbeJob<ProbePayload>))!;

        Assert.Equal(typeof(ProbeJob<ProbePayload>), registry.Resolve(key));
    }

    [Fact]
    public void PreferTheDeclaredKey_OverDerivation() {
        var registry = new DefaultScheduledJobRegistry();

        Assert.Equal(DeclaredProbeJob.JobKey, registry.ResolveKey(typeof(DeclaredProbeJob)));
        Assert.Equal(typeof(DeclaredProbeJob), registry.Resolve(DeclaredProbeJob.JobKey));
    }

    [Fact]
    public void PreferAKeyResolver_OverTheDeclaredKey() {
        var resolver = new Mock<IScheduledJobKeyResolver>();
        resolver.Setup(r => r.ResolveKey(typeof(DeclaredProbeJob))).Returns("resolver.owned");

        var registry = new DefaultScheduledJobRegistry([resolver.Object]);

        Assert.Equal("resolver.owned", registry.ResolveKey(typeof(DeclaredProbeJob)));
    }

    [Fact]
    public async Task ArmEveryPersistedJob_UnderItsRegistryKey() {
        var armed = new List<SchemataJob>();

        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                 .Callback((SchemataJob job, CancellationToken _) => armed.Add(job))
                 .Returns(Task.CompletedTask);

        var options = new SchemataSchedulingOptions();
        options.Jobs.Add(new(typeof(ProbeJob<ProbePayload>), new CronSchedule("0 * * * *")));
        options.Jobs.Add(new(typeof(DeclaredProbeJob), new CronSchedule("0 * * * *")));

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(options), EmptyRepositories(),
                                                    new DefaultScheduledJobRegistry());
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;
        await initializer.StopAsync(CancellationToken.None);

        Assert.Equal(2, armed.Count);
        Assert.All(armed, job => Assert.Equal(job.JobKey, job.Name));
        Assert.Equal(["Schemata.Scheduling.Tests.ProbeJob.ProbePayload", DeclaredProbeJob.JobKey],
                     armed.Select(job => job.Name));
    }

    private static IServiceProvider EmptyRepositories() {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ToAsync<SchemataJobExecution>([]));

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>()))
            .Returns(ToAsync<SchemataJob>([]));

        return new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object).BuildServiceProvider();
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}

public sealed class ProbePayload;

public sealed class ProbeJob<TPayload> : IScheduledJob
    where TPayload : class
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
}

[ScheduledJob(JobKey)]
public sealed class DeclaredProbeJob : IScheduledJob
{
    public const string JobKey = "schemata.tests.declared";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
}

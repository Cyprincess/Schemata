using Schemata.Scheduling.Tests.Fixtures;
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
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class ScheduledJobIdentityShould
{
    private static readonly char[] Unaddressable = ['`', '[', ']', ',', '=', ' ', '/'];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reject_Conflicting_Key_Without_Changing_Either_Mapping(bool reverse) {
        var registry = new DefaultScheduledJobRegistry();
        var first = reverse ? typeof(DeclaredProbeJob) : typeof(ProbeJob<ProbePayload>);
        var second = reverse ? typeof(ProbeJob<ProbePayload>) : typeof(DeclaredProbeJob);
        registry.Register(first, "shared");
        registry.Register(first, "shared");
        Assert.Throws<InvalidOperationException>(() => registry.Register(second, "shared"));
        Assert.Equal(first, registry.Resolve("shared"));
        Assert.Equal("shared", registry.ResolveKey(first));
        var other = registry.ResolveKey(second);
        Assert.NotEqual("shared", other);
        Assert.Equal(second, registry.Resolve(other!));
    }

    [Fact]
    public void Reject_Second_Key_For_One_Type_Without_Leaving_An_Alias() {
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<DeclaredProbeJob>("first");
        Assert.Throws<InvalidOperationException>(() => registry.Register<DeclaredProbeJob>("second"));
        Assert.Null(registry.Resolve("second"));
        Assert.Equal("first", registry.ResolveKey(typeof(DeclaredProbeJob)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolver_Conflicts_Preserve_Accepted_Inverse_Indexes(bool typeFirst) {
        var resolver = new Mock<IScheduledJobKeyResolver>();
        resolver.Setup(r => r.ResolveKey(typeof(DeclaredProbeJob))).Returns("resolver-key");
        resolver.Setup(r => r.ResolveType("resolver-key")).Returns(typeof(DeclaredProbeJob));
        resolver.Setup(r => r.ResolveType("second-key")).Returns(typeof(DeclaredProbeJob));
        var registry = new DefaultScheduledJobRegistry([resolver.Object]);
        if (typeFirst) registry.ResolveKey(typeof(DeclaredProbeJob));
        else registry.Resolve("resolver-key");
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("second-key"));
        Assert.Equal("resolver-key", registry.ResolveKey(typeof(DeclaredProbeJob)));
        Assert.Equal(typeof(DeclaredProbeJob), registry.Resolve("resolver-key"));
        resolver.Setup(r => r.ResolveKey(typeof(ProbeJob<ProbePayload>))).Returns("resolver-key");
        Assert.Throws<InvalidOperationException>(() => registry.ResolveKey(typeof(ProbeJob<ProbePayload>)));
        resolver.Setup(r => r.ResolveKey(typeof(ProbeJob<ProbePayload>))).Returns("other-key");
        Assert.Equal("other-key", registry.ResolveKey(typeof(ProbeJob<ProbePayload>)));
        Assert.Equal(typeof(ProbeJob<ProbePayload>), registry.Resolve("other-key"));
        Assert.Equal(typeof(DeclaredProbeJob), registry.Resolve("resolver-key"));
    }

    [Fact]
    public void Bulk_Registration_Preserves_An_Explicit_Accepted_Key() {
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<DeclaredProbeJob>("explicit");
        registry.RegisterAll([typeof(DeclaredProbeJob)]);
        Assert.Equal("explicit", registry.ResolveKey(typeof(DeclaredProbeJob)));
        Assert.Equal(typeof(DeclaredProbeJob), registry.Resolve("explicit"));
    }

    [Fact]
    public async Task Initial_Periodic_Schedule_Uses_Selected_Clock() {
        var instant = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(instant);
        SchemataJob? armed = null;
        var scheduler = new Mock<IScheduler<SchemataJob, SchemataJobExecution>>();
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
            .Callback<SchemataJob, CancellationToken>((job, _) => armed = job).Returns(Task.CompletedTask);
        var options = new SchemataSchedulingOptions();
        options.Jobs.Add(new(typeof(DeclaredProbeJob), new PeriodicSchedule(TimeSpan.FromMinutes(3))));
        var initializer = new SchedulingInitializer<SchemataJob, SchemataJobExecution>(scheduler.Object, Options.Create(options), EmptyRepositories(),
            new DefaultScheduledJobRegistry(), time: clock.Object);
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;
        Assert.NotNull(armed);
        Assert.Equal(instant.UtcDateTime.AddMinutes(3), armed.NextRunTime);
        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void DropAssemblyQualificationAndArity_FromAClosedGenericJobKey() {
        var key = new DefaultScheduledJobRegistry().ResolveKey(typeof(ProbeJob<ProbePayload>));

        Assert.Equal("Schemata.Scheduling.Tests.Fixtures.ProbeJob.ProbePayload", key);
    }

    [Fact]
    public void Keep_A_Closed_Generic_Job_Key_Addressable() {
        var key = new DefaultScheduledJobRegistry().ResolveKey(typeof(ProbeJob<ProbePayload>));

        Assert.NotNull(key);

        Assert.True(key.Length < 128, $"Derived key is {key.Length} characters: {key}");
        Assert.Equal(-1, key.IndexOfAny(Unaddressable));
    }

    [Fact]
    public void Resolve_A_Closed_Generic_Job_Back_From_Its_Derived_Key() {
        var registry = new DefaultScheduledJobRegistry();
        var key      = registry.ResolveKey(typeof(ProbeJob<ProbePayload>));

        Assert.NotNull(key);

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
    public async Task ArmConfiguredJobs_ByScheduleSlot_WithoutAssigningResourceNames() {
        var armed = new List<SchemataJob>();

        var scheduler = new Mock<IScheduler<SchemataJob, SchemataJobExecution>>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                 .Callback((SchemataJob job, CancellationToken _) => armed.Add(job))
                 .Returns(Task.CompletedTask);

        var options = new SchemataSchedulingOptions();
        options.Jobs.Add(new(typeof(ProbeJob<ProbePayload>), new CronSchedule("0 * * * *")));
        options.Jobs.Add(new(typeof(DeclaredProbeJob), new CronSchedule("0 * * * *")));

        var initializer = new SchedulingInitializer<SchemataJob, SchemataJobExecution>(scheduler.Object, Options.Create(options), EmptyRepositories(),
                                                    new DefaultScheduledJobRegistry());
        await initializer.StartAsync(CancellationToken.None);
        Assert.NotNull(initializer.ExecuteTask);
        await initializer.ExecuteTask!;
        await initializer.StopAsync(CancellationToken.None);

        Assert.Equal(2, armed.Count);
        Assert.All(armed, job => {
            Assert.Null(job.Name);
            Assert.Equal($"registration:{job.JobKey}", job.Key);
        });
        Assert.Equal(["Schemata.Scheduling.Tests.Fixtures.ProbeJob.ProbePayload", DeclaredProbeJob.JobKey],
                     armed.Select(job => job.JobKey));
    }

    private static IServiceProvider EmptyRepositories() {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(ToAsync<SchemataJobExecution>([]));

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(ToAsync<SchemataJob>([]));

        return new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
                                      .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                      .AddSchemataSchedulingRepositoryStore().BuildServiceProvider();
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}
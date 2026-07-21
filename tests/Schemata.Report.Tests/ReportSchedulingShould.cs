using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Scheduling;
using Schemata.Report.Scheduling.Advisors;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportSchedulingShould
{
    [Fact]
    public async Task Initializer_Arms_Periodic_Reports() {
        var scheduler = CreateScheduler();
        var initializer = new ReportSchedulingInitializer(
            new StaticReportDefinitionStore([PeriodicReport("daily", "0 0 * * *")]),
            scheduler.Object);

        await initializer.StartAsync(CancellationToken.None);

        VerifySchedule(scheduler, "daily", "0 0 * * *", Times.Once());
    }

    [Fact]
    public async Task Initializer_Arms_Both_Dsl_And_Db_Periodic_Reports() {
        var scheduler = CreateScheduler();
        var records = new List<SchemataReport> { PeriodicReport("database", "0 * * * *") };
        var services = new ServiceCollection();
        var reports = new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            new SchemataOptions(),
            services);
        reports.Define("dsl", definition => definition.Periodic(cron: "0 0 * * *"));
        services.AddScoped<IRepository<SchemataReport>>(_ => new ReportTestRepository<SchemataReport>(records));
        AddDefinitionSource<ConfigurationReportDefinitionStore>(services);
        AddDefinitionSource<DatabaseReportDefinitionStore<SchemataReport>>(services);
        services.AddSingleton<IReportDefinitionStore, CompositeReportDefinitionStore>();
        using var provider = services.BuildServiceProvider();
        var initializer = new ReportSchedulingInitializer(
            provider.GetRequiredService<IReportDefinitionStore>(),
            scheduler.Object);

        await initializer.StartAsync(CancellationToken.None);

        VerifySchedule(scheduler, "dsl", "0 0 * * *", Times.Once());
        VerifySchedule(scheduler, "database", "0 * * * *", Times.Once());
    }

    [Fact]
    public async Task Non_Periodic_Reports_Not_Armed() {
        var scheduler = CreateScheduler();
        var initializer = new ReportSchedulingInitializer(
            new StaticReportDefinitionStore([new() { Name = "manual", Periodic = false }]),
            scheduler.Object);

        await initializer.StartAsync(CancellationToken.None);

        scheduler.Verify(
            value => value.ScheduleAsync(
                It.IsAny<SchemataJob>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Definition_Change_Reschedules() {
        var scheduler = CreateScheduler();
        scheduler.Setup(value => value.UnscheduleAsync("jobs/report-daily", It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        var advisor = new AdviceReportScheduleSync<SchemataReport>(scheduler.Object);
        var changes = new CommitChanges<SchemataReport> {
            Updated = [PeriodicReport("daily", "0 6 * * *")],
        };

        var result = await advisor.AdviseAsync(
            new(new ReportTestServiceProvider()),
            Mock.Of<IRepository<SchemataReport>>(),
            changes,
            CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        scheduler.Verify(value => value.UnscheduleAsync("jobs/report-daily", It.IsAny<CancellationToken>()), Times.Once);
        VerifySchedule(scheduler, "daily", "0 6 * * *", Times.Once());
    }

    [Fact]
    public async Task Definition_Removal_Unschedules() {
        var scheduler = CreateScheduler();
        scheduler.Setup(value => value.UnscheduleAsync("jobs/report-daily", It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        var advisor = new AdviceReportScheduleSync<SchemataReport>(scheduler.Object);

        await advisor.AdviseAsync(
            new(new ReportTestServiceProvider()),
            Mock.Of<IRepository<SchemataReport>>(),
            new() { Removed = [PeriodicReport("daily", "0 0 * * *")] },
            CancellationToken.None);

        scheduler.Verify(value => value.UnscheduleAsync("jobs/report-daily", It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(
            value => value.ScheduleAsync(
                It.IsAny<SchemataJob>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Fire_Invokes_Generation_For_Named_Report() {
        var service = new CapturingReportService();
        using var provider = ReportTestHost.Create(
            new ReportMaterializerProbe(ReportTestRows.Create(1)),
            configure: services => services.AddSingleton<IReportService>(service));
        var job = new ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SchemataReportOptions>>());

        await job.ExecuteAsync(new JobContext {
            Variables = new Dictionary<string, string?> { ["report"] = "daily" },
        }, CancellationToken.None);

        Assert.Equal("daily", service.Request!.Name);
        Assert.True(service.Request.Persist);
    }

    [Fact]
    public async Task Missing_CronExpression_Throws_Clear_Error() {
        var scheduler = CreateScheduler();
        var initializer = new ReportSchedulingInitializer(
            new StaticReportDefinitionStore([new() {
                Name         = "daily",
                Periodic     = true,
                ScheduleKind = ReportScheduleKind.Cron,
            }]),
            scheduler.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.StartAsync(CancellationToken.None));

        Assert.Equal("Periodic report 'daily' requires a cron expression.", exception.Message);
    }

    private static Mock<IScheduler> CreateScheduler() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(value => value.ScheduleAsync(
                     It.IsAny<SchemataJob>(),
                     It.IsAny<IReadOnlyDictionary<string, string?>>(),
                     It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        return scheduler;
    }

    private static SchemataReport PeriodicReport(string name, string expression) {
        return new() {
            Name           = name,
            Periodic       = true,
            ScheduleKind   = ReportScheduleKind.Cron,
            CronExpression = expression,
        };
    }

    private static void AddDefinitionSource<TSource>(IServiceCollection services)
        where TSource : class {
        var source = typeof(TSource).GetInterface("Schemata.Report.Foundation.Definitions.IReportDefinitionSource")
                     ?? throw new InvalidOperationException("Report definition source contract is unavailable.");
        services.AddSingleton(source, typeof(TSource));
    }

    private static void VerifySchedule(Mock<IScheduler> scheduler, string name, string expression, Times times) {
        scheduler.Verify(
            value => value.ScheduleAsync(
                It.Is<SchemataJob>(job => job.Name == $"report-{name}"
                                          && job.CanonicalName == $"jobs/report-{name}"
                                          && job.JobKey == ReportJobKeyResolver.Key
                                          && job.ScheduleType == ScheduleType.Cron
                                          && job.CronExpression == expression),
                It.Is<IReadOnlyDictionary<string, string?>>(variables => variables.ContainsKey("report")
                                                                    && variables["report"] == name),
                It.IsAny<CancellationToken>()),
            times);
    }

    private sealed class StaticReportDefinitionStore(IEnumerable<SchemataReport> reports) : IReportDefinitionStore
    {
        public ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(string name, CancellationToken ct) {
            return ValueTask.FromResult<(SchemataReport Report, QueryInsightRequest Query)?>(null);
        }

        public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync([EnumeratorCancellation] CancellationToken ct) {
            foreach (var report in reports) {
                ct.ThrowIfCancellationRequested();
                if (report.Periodic) {
                    yield return report;
                }

                await Task.CompletedTask;
            }
        }
    }

    private sealed class CapturingReportService : IReportService
    {
        internal ReportRequest? Request { get; private set; }

        public ValueTask<ReportResult> RunAsync(ReportRequest request, ClaimsPrincipal? principal = null, CancellationToken ct = default) {
            Request = request;
            return ValueTask.FromResult(new ReportResult());
        }

        public ValueTask<Schemata.Abstractions.Resource.Operation> GenerateAsync(ReportRequest request, CancellationToken ct) {
            throw new NotSupportedException();
        }
    }
}

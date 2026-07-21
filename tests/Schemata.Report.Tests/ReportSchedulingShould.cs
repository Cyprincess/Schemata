using System;
using System.Collections.Generic;
using System.Linq;
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
            CreateDefinitionStore(PeriodicReport("daily", "0 0 * * *")).Object,
            scheduler.Object);

        await initializer.StartAsync(CancellationToken.None);

        VerifySchedule(scheduler, "daily", "0 0 * * *", Times.Once());
    }

    [Fact]
    public async Task Initializer_Arms_Both_Dsl_And_Db_Periodic_Reports() {
        var scheduler = CreateScheduler();
        var records = new List<SchemataReport> { PeriodicReport("database", "0 * * * *") };
        var persistence = new ReportPersistenceState();
        var services = new ServiceCollection();
        var reports = new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            new SchemataOptions(),
            services);
        reports.Define("dsl", definition => definition.Periodic(cron: "0 0 * * *"));
        services.AddScoped<IRepository<SchemataReport>>(_ => persistence.CreateRepository(records));
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
            CreateDefinitionStore(new SchemataReport { Name = "manual", Periodic = false }).Object,
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
            new(EmptyServices()),
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
            new(EmptyServices()),
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
        var service = ReportTestHost.CreateReportService(new());
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => services.AddSingleton(service.Object));
        var job = new ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SchemataReportOptions>>());

        await job.ExecuteAsync(new JobContext {
            Variables = new Dictionary<string, string?> { ["report"] = "daily" },
        }, CancellationToken.None);

        service.Verify(
            value => value.RunAsync(
                It.Is<ReportRequest>(request => request.Name == "daily" && request.Persist),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Missing_CronExpression_Throws_Clear_Error() {
        var scheduler = CreateScheduler();
        var initializer = new ReportSchedulingInitializer(
            CreateDefinitionStore(new SchemataReport {
                Name         = "daily",
                Periodic     = true,
                ScheduleKind = ReportScheduleKind.Cron,
            }).Object,
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

    private static IServiceProvider EmptyServices() {
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        services.Setup(value => value.GetService(It.IsAny<Type>())).Returns((object?)null);
        return services.Object;
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

    private static Mock<IReportDefinitionStore> CreateDefinitionStore(params SchemataReport[] reports) {
        var store = new Mock<IReportDefinitionStore>(MockBehavior.Strict);
        store.Setup(value => value.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(ValueTask.FromResult<(SchemataReport Report, QueryInsightRequest Query)?>(null));
        store.Setup(value => value.ListPeriodicAsync(It.IsAny<CancellationToken>()))
             .Returns((CancellationToken _) => ReportTestRows.ToAsync(reports.Where(report => report.Periodic)));
        return store;
    }
}

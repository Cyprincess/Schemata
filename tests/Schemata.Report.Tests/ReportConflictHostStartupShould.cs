using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Report.Foundation;
using Schemata.Report.Scheduling;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using Schemata.Report.Tests.Fixtures;

namespace Schemata.Report.Tests;

public class ReportConflictHostStartupShould
{
    [Fact]
    public async Task Conflict_Host_Builds_But_Hosted_Startup_Fails_Through_Real_Report_Use() {
        var scheduler = new Mock<IScheduler>(MockBehavior.Strict);
        scheduler.Setup(value => value.ScheduleAsync(
                      It.IsAny<SchemataJob>(),
                      It.IsAny<IReadOnlyDictionary<string, string?>>(),
                      It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        var records = new List<SchemataReport> { PeriodicReport("daily", "0 0 * * *") };
        var persistence = new ReportPersistenceState();
        var schemata = new SchemataOptions();
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddScoped<IRepository<SchemataReport>>(_ => persistence.CreateRepository(records));
        services.AddSingleton(scheduler.Object);
        services.AddHostedService<ReportSchedulingInitializer>();
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(schemata);
        services.AddSchemataReport<ConflictingReport, ConflictingSnapshot, ConflictingChunk>(schemata);

        using var host = builder.Build();

        await Assert.ThrowsAsync<FailedPreconditionException>(() => host.StartAsync(CancellationToken.None));
        scheduler.Verify(value => value.ScheduleAsync(
                             It.IsAny<SchemataJob>(),
                             It.IsAny<IReadOnlyDictionary<string, string?>>(),
                             It.IsAny<CancellationToken>()),
                         Times.Never);
    }

    [Fact]
    public async Task Same_Triple_Repetition_Arms_Each_Periodic_Report_Once() {
        var scheduler = new Mock<IScheduler>(MockBehavior.Strict);
        scheduler.Setup(value => value.ScheduleAsync(
                      It.IsAny<SchemataJob>(),
                      It.IsAny<IReadOnlyDictionary<string, string?>>(),
                      It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        var records = new List<SchemataReport> { PeriodicReport("daily", "0 0 * * *") };
        var persistence = new ReportPersistenceState();
        var schemata = new SchemataOptions();
        var services = new ServiceCollection();
        services.AddScoped<IRepository<SchemataReport>>(_ => persistence.CreateRepository(records));
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(schemata);
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(schemata);

        using var provider = services.BuildServiceProvider();
        var initializer = new ReportSchedulingInitializer(
            provider.GetRequiredService<IReportDefinitionStore>(),
            scheduler.Object);

        await initializer.StartAsync(CancellationToken.None);

        scheduler.Verify(value => value.ScheduleAsync(
                             It.Is<SchemataJob>(job => job.Key == "report:daily"),
                             It.IsAny<IReadOnlyDictionary<string, string?>>(),
                             It.IsAny<CancellationToken>()),
                         Times.Once);
    }

    [Abstractions.Entities.CanonicalName("reports/{report}")]
    private sealed class ConflictingReport : SchemataReport;

    [Abstractions.Entities.CanonicalName("reports/{report}/snapshots/{snapshot}")]
    private sealed class ConflictingSnapshot : SchemataReportSnapshot;

    [Abstractions.Entities.CanonicalName("reports/{report}/snapshots/{snapshot}/chunks/{chunk}")]
    private sealed class ConflictingChunk : SchemataReportSnapshotChunk;

    private static SchemataReport PeriodicReport(string name, string expression) {
        return new() {
            Name           = name,
            Periodic       = true,
            ScheduleKind   = Schemata.Report.Skeleton.Enums.ReportScheduleKind.Cron,
            CronExpression = expression,
        };
    }
}

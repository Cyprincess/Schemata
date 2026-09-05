using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Abstractions.Advisors;
using Schemata.Report.Skeleton.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Actor.Tests.Fixtures;

/// <summary>
///     Builds a bare-<see cref="ServiceCollection" /> Report host over a shared-cache in-memory SQLite
///     database, with or without the Report.Actor bridge installed. The report definition
///     <c>records</c> retains one snapshot, so concurrent generations race the retention "list then
///     trim" step. Each resolved <see cref="TestDbContext" /> opens its own connection against the
///     shared-cache URI, so concurrent scopes issue genuinely overlapping reads and writes; the
///     anchor <see cref="Connection" /> only keeps the shared-cache database alive.
/// </summary>
public sealed class ReportActorConcurrencyHarness : IAsyncDisposable
{
    public const string ReportName = "records";

    public required SqliteConnection Connection { get; init; }
    public required ServiceProvider  Root       { get; init; }

    /// <param name="withActor">Installs the Report.Actor bridge when true; otherwise the control-group, unwrapped path.</param>
    /// <param name="snapshotAdvisor">Optional extra <see cref="IReportSnapshotAdvisor" /> installed on the root provider.</param>
    public static async Task<ReportActorConcurrencyHarness> BuildAsync(bool withActor, IReportSnapshotAdvisor? snapshotAdvisor = null) {
        var connectionString = $"Data Source=file:{Guid.NewGuid():n}?mode=memory&cache=shared";
        var connection       = new SqliteConnection(connectionString);
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connectionString)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SourceRecord, EfCoreRepository<TestDbContext, SourceRecord>>();
        services.AddRepository<SchemataReport, EfCoreRepository<TestDbContext, SchemataReport>>();
        services.AddRepository<SchemataReportSnapshot, EfCoreRepository<TestDbContext, SchemataReportSnapshot>>();
        services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<TestDbContext, SchemataReportSnapshotChunk>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReport>, AdviceAddReportName>());

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseInsight(insight => {
            insight.UseAip().UseCel().UseOrdering();
            insight.AddRepositorySource("source-records", "source-records");
            insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
        });
        var reports = builder.UseReport(options => options.ChunkSize = 2);
        reports.Define(ReportName, definition => definition
            .From("source-records", "record")
            .Select("value")
            .Retain(count: 1));
        if (withActor) {
            builder.UseActor();
            reports.UseActor();
        }

        builder.Invoke(services);
        if (snapshotAdvisor is not null) {
            services.AddSingleton<IReportSnapshotAdvisor>(snapshotAdvisor);
        }

        var root = services.BuildServiceProvider();

        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.SourceRecords.AddRange(
                new SourceRecord { Uid = Guid.NewGuid(), Name = "one", Value = 1 },
                new SourceRecord { Uid = Guid.NewGuid(), Name = "two", Value = 2 },
                new SourceRecord { Uid = Guid.NewGuid(), Name = "three", Value = 3 });
            await db.SaveChangesAsync();
        }

        return new() { Connection = connection, Root = root };
    }

    #region IAsyncDisposable Members

    public async ValueTask DisposeAsync() {
        await Root.DisposeAsync();
        await Connection.DisposeAsync();
    }

    #endregion
}
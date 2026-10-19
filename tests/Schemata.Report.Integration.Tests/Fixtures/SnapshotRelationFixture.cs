using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using Grpc.Net.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Models;

namespace Schemata.Report.Integration.Tests.Fixtures;

internal sealed class SnapshotRelationFixture : IAsyncDisposable
{
    private readonly WebAppFactory _factory;

    internal SnapshotRelationFixture(Action<IServiceCollection>? configure = null) {
        _factory = new(services => {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshot>, AdviceAddDailySnapshotName>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshotChunk>, AdviceAddDailyChunkName>());
            services.Configure<SchemataReportOptions>(options => options.ChunkSize = 2);
            configure?.Invoke(services);
        });
    }
    private string? _database;

    internal IServiceProvider Services {
        get {
            var services = _factory.Services;
            if (_database is null) {
                using var scope = services.CreateScope();
                _database = scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.GetDbConnection().DataSource;
            }

            return services;
        }
    }

    internal HttpClient CreateClient() => _factory.CreateClient();

    internal GrpcChannel CreateGrpcChannel() => _factory.CreateGrpcChannel();

    internal async Task CreateAsync() {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataReport>>();
        var definition = JsonSerializer.Serialize(new QueryInsightRequest {
            Sources = [new("record", "source-records")],
            Selections = [new() { Field = "value" }],
            Transformations = [new() { OrderBy = new("value") }],
        }, SchemataJson.Default);
        foreach (var name in new[] { "A", "B" }) {
            await mutation.CreateAsync(new() { Name = name, Definition = definition });
        }

        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        await reports.RunAsync(new() { Name = "reports/A", Persist = true });
        foreach (var row in await database.SourceRecords.ToListAsync()) {
            row.Value += 100;
        }
        await database.SaveChangesAsync();
        await reports.RunAsync(new() { Name = "reports/B", Persist = true });
    }

    internal async Task<SchemataReport> RetainAsync(string name, ReportRetention retention) {
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataReport>>();
        var report = await repository.FirstOrDefaultAsync(query => query.Where(row => row.Name == name))
                     ?? throw new InvalidOperationException("The relation fixture report is missing.");
        report.Retention = retention;
        await scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataReport>>().UpdateAsync(report);
        return report;
    }

    public async ValueTask DisposeAsync() {
        await _factory.DisposeAsync();
        if (_database is not null) {
            using var connection = new SqliteConnection($"Data Source={_database}");
            SqliteConnection.ClearPool(connection);
            File.Delete(_database);
        }
    }
}
